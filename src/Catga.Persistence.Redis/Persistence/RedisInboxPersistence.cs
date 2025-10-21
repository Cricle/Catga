using Catga.Abstractions;
using Catga.Inbox;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Persistence;

/// <summary>
/// Redis Inbox 持久化存储 - 专注于存储，不涉及传输
/// </summary>
public class RedisInboxPersistence : RedisStoreBase, IInboxStore
{
    private readonly ILogger<RedisInboxPersistence> _logger;

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
        RedisInboxOptions? options = null)
        : base(redis, serializer, options?.KeyPrefix ?? "inbox")
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryLockMessageAsync(
        long messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
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

        var data = Serializer.Serialize(message);

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
        }
        else
        {
            _logger.LogDebug("Message {MessageId} already processed or locked", messageId);
        }

        return locked;
    }

    public async ValueTask MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = GetMessageKey(message.MessageId);

        message.Status = InboxStatus.Processed;
        message.ProcessedAt = DateTime.UtcNow;
        message.LockExpiresAt = null;

        var data = Serializer.Serialize(message);

        // 保存已处理消息（保留 24 小时用于幂等性检查）
        await db.StringSetAsync(key, data, TimeSpan.FromHours(24));

        _logger.LogDebug("Marked message {MessageId} as processed", message.MessageId);
    }

    public async ValueTask<bool> HasBeenProcessedAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = GetMessageKey(messageId);

        return await db.KeyExistsAsync(key);
    }

    public async ValueTask<string?> GetProcessedResultAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = GetMessageKey(messageId);

        var data = await db.StringGetAsync(key);
        if (!data.HasValue)
            return null;

        var message = Serializer.Deserialize<InboxMessage>(data!);
        return message?.ProcessingResult;
    }

    public async ValueTask ReleaseLockAsync(
        long messageId,
        CancellationToken cancellationToken = default)
    {
        var db = GetDatabase();
        var key = GetMessageKey(messageId);

        // 删除锁定
        await db.KeyDeleteAsync(key);

        _logger.LogDebug("Released lock on message {MessageId}", messageId);
    }

    public ValueTask DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        // Redis 使用 TTL 自动清理，这里不需要额外操作
        _logger.LogDebug("Redis inbox uses TTL for cleanup");
        return default;
    }

    private string GetMessageKey(long messageId) => $"{KeyPrefix}:msg:{messageId}";
}

/// <summary>
/// Redis Inbox 持久化选项
/// </summary>
public class RedisInboxOptions
{
    public string KeyPrefix { get; set; } = "inbox";
}

