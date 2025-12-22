using System.Diagnostics;
using Catga.Abstractions;
using Catga.Inbox;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>Options for RedisInboxStore.</summary>
public class RedisInboxStoreOptions
{
    /// <summary>Key prefix for inbox entries. Default: inbox:</summary>
    public string KeyPrefix { get; set; } = "inbox:";
}

/// <summary>Redis-based inbox store for idempotent message processing.</summary>
public sealed class RedisInboxStore : RedisStoreBase, IInboxStore
{
    private readonly IResiliencePipelineProvider _provider;

    public RedisInboxStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        IOptions<RedisInboxStoreOptions>? options = null)
        : base(redis, serializer, options?.Value.KeyPrefix ?? "inbox:")
    {
        _provider = provider;
    }

    public async ValueTask<bool> TryLockMessageAsync(long messageId, TimeSpan lockDuration, CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceNoRetryAsync(async _ =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Redis.Inbox.TryLock", ActivityKind.Internal);
            var db = GetDatabase();
            var key = BuildKey(messageId);
            var lockKey = $"{key}:lock";

            // Check if already processed
            var statusBytes = await db.HashGetAsync(key, "Status");
            if (statusBytes.HasValue && (InboxStatus)(int)statusBytes == InboxStatus.Processed)
                return false;

            // Try to acquire lock using SET NX with expiry
            var lockAcquired = await db.StringSetAsync(lockKey, (RedisValue)DateTime.UtcNow.Ticks, lockDuration, When.NotExists);
            if (!lockAcquired)
            {
                // Check if existing lock is expired
                var existingLock = await db.StringGetAsync(lockKey);
                if (existingLock.HasValue)
                {
                    var lockTime = new DateTime((long)existingLock);
                    if (DateTime.UtcNow - lockTime > lockDuration)
                    {
                        // Lock expired, try to take over
                        await db.KeyDeleteAsync(lockKey);
                        lockAcquired = await db.StringSetAsync(lockKey, (RedisValue)DateTime.UtcNow.Ticks, lockDuration, When.NotExists);
                    }
                }
            }

            if (lockAcquired)
            {
                await db.HashSetAsync(key, [
                    new HashEntry("MessageId", messageId),
                    new HashEntry("Status", (RedisValue)(int)InboxStatus.Processing),
                    new HashEntry("LockExpiresAt", (RedisValue)DateTime.UtcNow.Add(lockDuration).Ticks)
                ]);
                CatgaDiagnostics.InboxLocksAcquired.Add(1);
            }

            return lockAcquired;
        }, ct);
    }

    public async ValueTask MarkAsProcessedAsync(InboxMessage message, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Redis.Inbox.MarkProcessed", ActivityKind.Internal);
            ArgumentNullException.ThrowIfNull(message);
            var db = GetDatabase();
            var key = BuildKey(message.MessageId);
            var lockKey = $"{key}:lock";

            var now = DateTime.UtcNow;
            var entries = new List<HashEntry>
            {
                new HashEntry("MessageId", message.MessageId),
                new HashEntry("MessageType", message.MessageType),
                new HashEntry("Payload", message.Payload),
                new HashEntry("Status", (RedisValue)(int)InboxStatus.Processed),
                new HashEntry("ReceivedAt", (RedisValue)message.ReceivedAt.Ticks),
                new HashEntry("ProcessedAt", (RedisValue)now.Ticks)
            };

            if (message.ProcessingResult != null)
                entries.Add(new HashEntry("ProcessingResult", message.ProcessingResult));
            if (message.CorrelationId.HasValue)
                entries.Add(new HashEntry("CorrelationId", message.CorrelationId.Value));
            if (message.Metadata != null)
                entries.Add(new HashEntry("Metadata", message.Metadata));

            await db.HashSetAsync(key, entries.ToArray());
            await db.KeyDeleteAsync(lockKey);
            CatgaDiagnostics.InboxProcessed.Add(1);
        }, ct);
    }

    public async ValueTask<bool> HasBeenProcessedAsync(long messageId, CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = GetDatabase();
            var key = BuildKey(messageId);
            var statusBytes = await db.HashGetAsync(key, "Status");
            return statusBytes.HasValue && (InboxStatus)(int)statusBytes == InboxStatus.Processed;
        }, ct);
    }

    public async ValueTask<byte[]?> GetProcessedResultAsync(long messageId, CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = GetDatabase();
            var key = BuildKey(messageId);
            var values = await db.HashGetAsync(key, ["Status", "ProcessingResult"]);
            if (values[0].HasValue && (InboxStatus)(int)values[0] == InboxStatus.Processed && values[1].HasValue)
                return (byte[])values[1]!;
            return null;
        }, ct);
    }

    public async ValueTask ReleaseLockAsync(long messageId, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = GetDatabase();
            var key = BuildKey(messageId);
            var lockKey = $"{key}:lock";

            await db.HashSetAsync(key, [
                new HashEntry("Status", (int)InboxStatus.Pending),
                new HashEntry("LockExpiresAt", RedisValue.Null)
            ]);
            await db.KeyDeleteAsync(lockKey);
            CatgaDiagnostics.InboxLocksReleased.Add(1);
        }, ct);
    }

    public async ValueTask DeleteProcessedMessagesAsync(TimeSpan retentionPeriod, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Redis.Inbox.DeleteProcessed", ActivityKind.Internal);
            var db = GetDatabase();
            var server = Redis.GetServers().FirstOrDefault();
            if (server == null) return;

            var cutoff = DateTime.UtcNow.Subtract(retentionPeriod).Ticks;
            var keysToDelete = new List<RedisKey>();

            await foreach (var key in server.KeysAsync(pattern: $"{KeyPrefix}*"))
            {
                if (key.ToString().EndsWith(":lock")) continue;
                var values = await db.HashGetAsync(key, ["Status", "ProcessedAt"]);
                if (values[0].HasValue && (InboxStatus)(int)values[0] == InboxStatus.Processed &&
                    values[1].HasValue && (long)values[1] < cutoff)
                {
                    keysToDelete.Add(key);
                    keysToDelete.Add($"{key}:lock");
                }
            }

            if (keysToDelete.Count > 0)
                await db.KeyDeleteAsync(keysToDelete.ToArray());
        }, ct);
    }
}
