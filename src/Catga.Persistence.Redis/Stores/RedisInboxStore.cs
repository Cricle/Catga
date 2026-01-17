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

    // Lua script for atomic lock acquisition with status check
    private const string TryLockScript = @"
        -- Check if already processed
        local status = redis.call('HGET', KEYS[1], 'Status')
        if status == '2' then return 0 end
        
        -- Try to acquire lock
        local lockKey = KEYS[2]
        local now = tonumber(ARGV[1])
        local lockDurationMs = tonumber(ARGV[2])
        
        -- Check if lock exists
        local existingLock = redis.call('GET', lockKey)
        if existingLock then
            local lockTime = tonumber(existingLock)
            -- Check if lock is expired
            if now - lockTime <= lockDurationMs then
                return 0  -- Lock still valid
            end
            -- Lock expired, delete it
            redis.call('DEL', lockKey)
        end
        
        -- Acquire lock with expiry
        redis.call('SET', lockKey, ARGV[1], 'PX', ARGV[2])
        
        -- Update message status
        redis.call('HSET', KEYS[1], 
            'MessageId', ARGV[3],
            'Status', '1',
            'LockExpiresAt', ARGV[4])
        
        return 1
    ";

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
            var now = DateTime.UtcNow.Ticks;
            var lockDurationMs = (long)lockDuration.TotalMilliseconds;
            var lockExpiresAt = DateTime.UtcNow.Add(lockDuration).Ticks;

            // Use Lua script for atomic lock acquisition
            var result = await db.ScriptEvaluateAsync(TryLockScript,
                [key, lockKey],
                [now.ToString(), lockDurationMs.ToString(), messageId.ToString(), lockExpiresAt.ToString()]);

            var lockAcquired = (long)result! == 1;
            if (lockAcquired)
            {
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
