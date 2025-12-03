using System.Diagnostics;
using Catga.Abstractions;
using Catga.Inbox;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Persistence;

/// <summary>Redis Inbox persistence store.</summary>
public class RedisInboxPersistence(IConnectionMultiplexer redis, IMessageSerializer serializer, ILogger<RedisInboxPersistence> logger, IResiliencePipelineProvider provider, RedisInboxOptions? options = null)
    : RedisStoreBase(redis, serializer, options?.KeyPrefix ?? "inbox"), IInboxStore
{
    private const string TryLockScript = "local e=redis.call('GET',KEYS[1]) if e then return 0 end redis.call('SET',KEYS[1],ARGV[3],'EX',ARGV[2]) return 1";

    public async ValueTask<bool> TryLockMessageAsync(
        long messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.TryLock", ActivityKind.Internal);
            var db = GetDatabase();
            var key = GetMessageKey(messageId);

            // 创建锁定消息
            var message = new InboxMessage
            {
                MessageId = messageId,
                MessageType = "", // 稍后更新
                Payload = Array.Empty<byte>(), // 稍后更新
                Status = InboxStatus.Processing,
                LockExpiresAt = DateTime.UtcNow.Add(lockDuration)
            };

            var data = Serializer.Serialize(message, typeof(InboxMessage));

            // 使用 Lua 脚本原子化检查并锁定
            var result = await db.ScriptEvaluateAsync(
                TryLockScript,
                new RedisKey[] { key },
                new RedisValue[]
                {
                    messageId,
                    (int)lockDuration.TotalSeconds,
                    data
                });

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

    public async ValueTask MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default)
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

            // 保存已处理消息（保留 24 小时用于幂等性检查）
            await db.StringSetAsync(key, data, TimeSpan.FromHours(24));

            CatgaLog.InboxMarkedProcessed(logger, message.MessageId);
            CatgaDiagnostics.InboxProcessed.Add(1);
        }, cancellationToken);
    }

    public async ValueTask<bool> HasBeenProcessedAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.HasBeenProcessed", ActivityKind.Internal);
            var db = GetDatabase();
            var key = GetMessageKey(messageId);

            var exists = await db.KeyExistsAsync(key);
            return exists;
        }, cancellationToken);
    }

    public async ValueTask<byte[]?> GetProcessedResultAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.GetProcessedResult", ActivityKind.Internal);
            var db = GetDatabase();
            var key = GetMessageKey(messageId);

            var data = await db.StringGetAsync(key);
            if (!data.HasValue)
                return null;

            var message = (InboxMessage?)Serializer.Deserialize((byte[])data!, typeof(InboxMessage));
            return message?.ProcessingResult;
        }, cancellationToken);
    }

    public async ValueTask ReleaseLockAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.ReleaseLock", ActivityKind.Internal);
            var db = GetDatabase();
            var key = GetMessageKey(messageId);

            // 删除锁定
            await db.KeyDeleteAsync(key);

            CatgaLog.InboxReleasedLock(logger, messageId);
            CatgaDiagnostics.InboxLocksReleased.Add(1);
        }, cancellationToken);
    }

    public ValueTask DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        return provider.ExecutePersistenceAsync(ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.DeleteProcessed", ActivityKind.Internal);
            // Redis 使用 TTL 自动清理，这里不需要额外操作
            CatgaLog.InboxTTL(logger);
            return ValueTask.CompletedTask;
        }, cancellationToken);
    }

    private string GetMessageKey(long messageId) => $"{KeyPrefix}:msg:{messageId}";
}



