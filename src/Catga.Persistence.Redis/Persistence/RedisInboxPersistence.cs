using System.Diagnostics;
using Catga.Abstractions;
using Catga.Inbox;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Persistence;

/// <summary>Redis Inbox persistence store.</summary>
public class RedisInboxPersistence(IConnectionMultiplexer redis, IMessageSerializer serializer, ILogger<RedisInboxPersistence> logger, IResiliencePipelineProvider provider, IOptions<RedisPersistenceOptions>? options = null)
    : RedisStoreBase(redis, serializer, options?.Value.InboxKeyPrefix ?? "catga:inbox:"), IInboxStore
{
    private const string TryLockScript = "local e=redis.call('GET',KEYS[1]) if e then return 0 end redis.call('SET',KEYS[1],ARGV[3],'EX',ARGV[2]) return 1";

    public async ValueTask<bool> TryLockMessageAsync(long messageId, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        // No retry for lock operations - they are not idempotent
        return await provider.ExecutePersistenceNoRetryAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.TryLock", ActivityKind.Internal);
            var db = GetDatabase();
            var key = GetMessageKey(messageId);

            var message = new InboxMessage { MessageId = messageId, MessageType = "", Payload = [], Status = InboxStatus.Processing, LockExpiresAt = DateTime.UtcNow.Add(lockDuration) };
            var data = Serializer.Serialize(message, typeof(InboxMessage));
            var result = await db.ScriptEvaluateAsync(TryLockScript, [key], [messageId, (RedisValue)(int)lockDuration.TotalSeconds, data]);
            var locked = (int)result == 1;

            if (locked)
            {
                CatgaLog.InboxLocked(logger, messageId);
                CatgaDiagnostics.InboxLocksAcquired.Add(1);
            }
            else
            {
                CatgaLog.InboxAlreadyProcessedOrLocked(logger, messageId);
            }
            return locked;
        }, cancellationToken);
    }

    public async ValueTask MarkAsProcessedAsync(InboxMessage message, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.MarkProcessed", ActivityKind.Internal);
            var db = GetDatabase();
            var key = GetMessageKey(message.MessageId);

            message.Status = InboxStatus.Processed;
            message.ProcessedAt = DateTime.UtcNow;
            message.LockExpiresAt = null;

            var data = Serializer.Serialize(message, typeof(InboxMessage));
            await db.StringSetAsync(key, data, TimeSpan.FromHours(24));
            CatgaLog.InboxMarkedProcessed(logger, message.MessageId);
            CatgaDiagnostics.InboxProcessed.Add(1);
        }, cancellationToken);
    }

    public async ValueTask<bool> HasBeenProcessedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            var db = GetDatabase();
            return await db.KeyExistsAsync(GetMessageKey(messageId));
        }, cancellationToken);
    }

    public async ValueTask<byte[]?> GetProcessedResultAsync(long messageId, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            var db = GetDatabase();
            var data = await db.StringGetAsync(GetMessageKey(messageId));
            if (!data.HasValue) return null;
            var message = (InboxMessage?)Serializer.Deserialize((byte[])data!, typeof(InboxMessage));
            return message?.ProcessingResult;
        }, cancellationToken);
    }

    public async ValueTask ReleaseLockAsync(long messageId, CancellationToken cancellationToken = default)
    {
        // No retry for lock release - should be atomic
        await provider.ExecutePersistenceNoRetryAsync(async ct =>
        {
            var db = GetDatabase();
            await db.KeyDeleteAsync(GetMessageKey(messageId));
            CatgaDiagnostics.InboxLocksReleased.Add(1);
        }, cancellationToken);
    }

    public ValueTask DeleteProcessedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    private string GetMessageKey(long messageId) => $"{KeyPrefix}msg:{messageId}";
}
