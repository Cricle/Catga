using Catga.Abstractions;
using Catga.Inbox;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Catga.Resilience;
using Catga.Persistence.Redis;
using System.Diagnostics;
using Catga.Observability;

namespace Catga.Persistence.Redis.Persistence;

/// <summary>
/// Redis Inbox 持久化存储 - 专注于存储，不涉及传输
/// </summary>
public class RedisInboxPersistence : RedisStoreBase, IInboxStore
{
    private readonly ILogger<RedisInboxPersistence> _logger;
    private readonly IResiliencePipelineProvider _provider;

    // Lua 脚本：原子化尝试锁定消息
    private const string TryLockScript = @"
        local msgKey = KEYS[1]
        local messageId = ARGV[1]
        local lockExpires = tonumber(ARGV[2])
        local newMsg = ARGV[3]

        local existing = redis.call('GET', msgKey)
        if existing then
            return 0  -- 已存在，锁定失败
        end

        -- 设置消息和锁定过期时间
        redis.call('SET', msgKey, newMsg, 'EX', lockExpires)
        return 1  -- 锁定成功
    ";

    public RedisInboxPersistence(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        ILogger<RedisInboxPersistence> logger,
        RedisInboxOptions? options = null,
        IResiliencePipelineProvider? provider = null)
        : base(redis, serializer, options?.KeyPrefix ?? "inbox")
    {
        _logger = logger;
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async ValueTask<bool> TryLockMessageAsync(
        long messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.TryLock", ActivityKind.Internal);
            var db = GetDatabase();
            var key = GetMessageKey(messageId);

            // 创建锁定消息
            var message = new InboxMessage
            {
                MessageId = messageId,
                MessageType = "", // 稍后更新
                Payload = "", // 稍后更新
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
                _logger.LogDebug("Locked message {MessageId} for processing", messageId);
                CatgaDiagnostics.InboxLocksAcquired.Add(1);
            }
            else
            {
                _logger.LogDebug("Message {MessageId} already processed or locked", messageId);
            }

            return locked;
        }, cancellationToken);
    }

    public async ValueTask MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default)
    {
        await _provider.ExecutePersistenceAsync(async ct =>
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

            _logger.LogDebug("Marked message {MessageId} as processed", message.MessageId);
            CatgaDiagnostics.InboxProcessed.Add(1);
        }, cancellationToken);
    }

    public async ValueTask<bool> HasBeenProcessedAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.HasBeenProcessed", ActivityKind.Internal);
            var db = GetDatabase();
            var key = GetMessageKey(messageId);

            var exists = await db.KeyExistsAsync(key);
            return exists;
        }, cancellationToken);
    }

    public async ValueTask<string?> GetProcessedResultAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
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
        await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.ReleaseLock", ActivityKind.Internal);
            var db = GetDatabase();
            var key = GetMessageKey(messageId);

            // 删除锁定
            await db.KeyDeleteAsync(key);

            _logger.LogDebug("Released lock on message {MessageId}", messageId);
            CatgaDiagnostics.InboxLocksReleased.Add(1);
        }, cancellationToken);
    }

    public ValueTask DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        return _provider.ExecutePersistenceAsync(ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Inbox.DeleteProcessed", ActivityKind.Internal);
            // Redis 使用 TTL 自动清理，这里不需要额外操作
            _logger.LogDebug("Redis inbox uses TTL for cleanup");
            return ValueTask.CompletedTask;
        }, cancellationToken);
    }

    private string GetMessageKey(long messageId) => $"{KeyPrefix}:msg:{messageId}";
}



