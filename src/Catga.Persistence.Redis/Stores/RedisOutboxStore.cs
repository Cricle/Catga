using System.Diagnostics;
using Catga.Abstractions;
using Catga.Hosting;
using Catga.Observability;
using Catga.Outbox;
using Catga.Resilience;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>Options for RedisOutboxStore.</summary>
public class RedisOutboxStoreOptions
{
    /// <summary>Key prefix for outbox entries. Default: outbox:</summary>
    public string KeyPrefix { get; set; } = "outbox:";
    /// <summary>Sorted set key for pending messages. Default: outbox:pending</summary>
    public string PendingSetKey { get; set; } = "outbox:pending";
}

/// <summary>Redis-based outbox store for reliable message publishing.</summary>
public sealed class RedisOutboxStore : RedisStoreBase, IOutboxStore, IHealthCheckable, Hosting.IRecoverableComponent
{
    private readonly IResiliencePipelineProvider _provider;
    private readonly string _pendingSetKey;
    private bool _isHealthy = true;
    private string? _healthStatus = "Initialized";
    private DateTimeOffset? _lastHealthCheck;
    
    /// <inheritdoc/>
    public bool IsHealthy => _isHealthy;
    
    /// <inheritdoc/>
    public string? HealthStatus => _healthStatus;
    
    /// <inheritdoc/>
    public DateTimeOffset? LastHealthCheck => _lastHealthCheck;
    
    /// <inheritdoc/>
    public string ComponentName => "RedisOutboxStore";

    public RedisOutboxStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        IOptions<RedisOutboxStoreOptions>? options = null)
        : base(redis, serializer, options?.Value.KeyPrefix ?? "outbox:")
    {
        _provider = provider;
        _pendingSetKey = options?.Value.PendingSetKey ?? "outbox:pending";
    }

    public async ValueTask AddAsync(OutboxMessage message, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Redis.Outbox.Add", ActivityKind.Producer);
            ArgumentNullException.ThrowIfNull(message);
            if (message.MessageId == 0) throw new ArgumentException("MessageId must be > 0");

            try
            {
                var db = GetDatabase();
                var key = BuildKey(message.MessageId);

                var entries = new List<HashEntry>
                {
                    new HashEntry("MessageId", message.MessageId),
                    new HashEntry("MessageType", message.MessageType),
                    new HashEntry("Payload", message.Payload),
                    new HashEntry("CreatedAt", (RedisValue)message.CreatedAt.Ticks),
                    new HashEntry("Status", (RedisValue)(int)OutboxStatus.Pending),
                    new HashEntry("RetryCount", (RedisValue)message.RetryCount),
                    new HashEntry("MaxRetries", (RedisValue)message.MaxRetries)
                };

                if (message.CorrelationId.HasValue)
                    entries.Add(new HashEntry("CorrelationId", message.CorrelationId.Value));
                if (message.Metadata != null)
                    entries.Add(new HashEntry("Metadata", message.Metadata));

                await db.HashSetAsync(key, entries.ToArray());
                await db.SortedSetAddAsync(_pendingSetKey, message.MessageId, (double)message.CreatedAt.Ticks);
                CatgaDiagnostics.OutboxAdded.Add(1);
                
                UpdateHealthStatus(true);
            }
            catch (Exception ex)
            {
                UpdateHealthStatus(false, ex.Message);
                throw;
            }
        }, ct);
    }

    public async ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int maxCount = 100, CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Redis.Outbox.GetPending", ActivityKind.Internal);
            var db = GetDatabase();
            var result = new List<OutboxMessage>();

            // Get message IDs from sorted set (ordered by CreatedAt)
            var messageIds = await db.SortedSetRangeByRankAsync(_pendingSetKey, 0, maxCount - 1);

            foreach (var idValue in messageIds)
            {
                var messageId = (long)idValue;
                var key = BuildKey(messageId);
                var hash = await db.HashGetAllAsync(key);
                if (hash.Length == 0) continue;

                var dict = hash.ToDictionary(x => x.Name.ToString(), x => x.Value);
                var status = (OutboxStatus)(int)dict.GetValueOrDefault("Status");
                var retryCount = (int)dict.GetValueOrDefault("RetryCount");
                var maxRetries = dict.TryGetValue("MaxRetries", out var mr) ? (int)mr : 3;

                if (status == OutboxStatus.Pending && retryCount < maxRetries)
                {
                    result.Add(new OutboxMessage
                    {
                        MessageId = messageId,
                        MessageType = dict.GetValueOrDefault("MessageType").ToString(),
                        Payload = (byte[])dict.GetValueOrDefault("Payload")!,
                        CreatedAt = new DateTime((long)dict.GetValueOrDefault("CreatedAt")),
                        Status = status,
                        RetryCount = retryCount,
                        MaxRetries = maxRetries,
                        LastError = dict.TryGetValue("LastError", out var le) ? le.ToString() : null,
                        CorrelationId = dict.TryGetValue("CorrelationId", out var ci) ? (long)ci : null,
                        Metadata = dict.TryGetValue("Metadata", out var md) ? md.ToString() : null
                    });
                }
            }

            return (IReadOnlyList<OutboxMessage>)result;
        }, ct);
    }

    public async ValueTask MarkAsPublishedAsync(long messageId, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Redis.Outbox.MarkPublished", ActivityKind.Internal);
            var db = GetDatabase();
            var key = BuildKey(messageId);

            await db.HashSetAsync(key, [
                new HashEntry("Status", (RedisValue)(int)OutboxStatus.Published),
                new HashEntry("PublishedAt", (RedisValue)DateTime.UtcNow.Ticks)
            ]);
            await db.SortedSetRemoveAsync(_pendingSetKey, messageId);
            CatgaDiagnostics.OutboxPublished.Add(1);
        }, ct);
    }

    public async ValueTask MarkAsFailedAsync(long messageId, string errorMessage, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Redis.Outbox.MarkFailed", ActivityKind.Internal);
            var db = GetDatabase();
            var key = BuildKey(messageId);

            var values = await db.HashGetAsync(key, ["RetryCount", "MaxRetries"]);
            var retryCount = values[0].HasValue ? (int)values[0] + 1 : 1;
            var maxRetries = values[1].HasValue ? (int)values[1] : 3;

            var newStatus = retryCount >= maxRetries ? OutboxStatus.Failed : OutboxStatus.Pending;

            await db.HashSetAsync(key, [
                new HashEntry("RetryCount", (RedisValue)retryCount),
                new HashEntry("LastError", errorMessage),
                new HashEntry("Status", (RedisValue)(int)newStatus)
            ]);

            if (newStatus == OutboxStatus.Failed)
                await db.SortedSetRemoveAsync(_pendingSetKey, messageId);

            CatgaDiagnostics.OutboxFailed.Add(1);
        }, ct);
    }

    public async ValueTask DeletePublishedMessagesAsync(TimeSpan retentionPeriod, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Redis.Outbox.DeletePublished", ActivityKind.Internal);
            var db = GetDatabase();
            var server = Redis.GetServers().FirstOrDefault();
            if (server == null) return;

            var cutoff = DateTime.UtcNow.Subtract(retentionPeriod).Ticks;
            var keysToDelete = new List<RedisKey>();

            await foreach (var key in server.KeysAsync(pattern: $"{KeyPrefix}*"))
            {
                if (key == _pendingSetKey) continue;
                var values = await db.HashGetAsync(key, ["Status", "PublishedAt"]);
                if (values[0].HasValue && (OutboxStatus)(int)values[0] == OutboxStatus.Published &&
                    values[1].HasValue && (long)values[1] < cutoff)
                {
                    keysToDelete.Add(key);
                }
            }

            if (keysToDelete.Count > 0)
                await db.KeyDeleteAsync(keysToDelete.ToArray());
        }, ct);
    }
    
    /// <inheritdoc/>
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify connection by attempting a simple operation
            var db = GetDatabase();
            await db.PingAsync();
            
            _isHealthy = true;
            _healthStatus = "Recovered successfully";
            _lastHealthCheck = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _isHealthy = false;
            _healthStatus = $"Recovery failed: {ex.Message}";
            _lastHealthCheck = DateTimeOffset.UtcNow;
            throw;
        }
    }
    
    /// <summary>
    /// Updates health status based on operation results
    /// </summary>
    private void UpdateHealthStatus(bool success, string? errorMessage = null)
    {
        _isHealthy = success;
        _healthStatus = success ? "Healthy" : $"Unhealthy: {errorMessage}";
        _lastHealthCheck = DateTimeOffset.UtcNow;
    }
}
