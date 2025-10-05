using Catga.Inbox;
using Catga.Redis.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Redis;

/// <summary>
/// Redis inbox store implementation - Lock-free optimized
/// </summary>
public class RedisInboxStore : IInboxStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisInboxStore> _logger;
    private readonly string _keyPrefix;
    private readonly string _lockKeyPrefix;

    // Lua 脚本：原子化检查+锁定操作（避免两次 Redis 调用）
    private const string TryLockScript = @"
        local msgKey = KEYS[1]
        local lockKey = KEYS[2]
        local lockValue = ARGV[1]
        local lockExpiry = tonumber(ARGV[2])

        -- 检查消息是否已处理
        local msgData = redis.call('GET', msgKey)
        if msgData then
            local status = string.match(msgData, '""status"":%s*""(%w+)""')
            if status == 'Processed' then
                return 0  -- 已处理，不能锁定
            end
        end

        -- 尝试获取锁（SET NX）
        local locked = redis.call('SET', lockKey, lockValue, 'EX', lockExpiry, 'NX')
        if locked then
            return 1  -- 锁定成功
        else
            return 0  -- 锁定失败
        end
    ";

    public RedisInboxStore(
        IConnectionMultiplexer redis,
        ILogger<RedisInboxStore> logger,
        RedisCatgaOptions? options = null)
    {
        _redis = redis;
        _logger = logger;
        _keyPrefix = options?.InboxKeyPrefix ?? "inbox:";
        _lockKeyPrefix = $"{_keyPrefix}lock:";
    }

    /// <inheritdoc/>
    public async Task<bool> TryLockMessageAsync(
        string messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(messageId))
            throw new ArgumentException("MessageId is required", nameof(messageId));

        var db = _redis.GetDatabase();
        var key = GetMessageKey(messageId);
        var lockKey = GetLockKey(messageId);

        // 使用 Lua 脚本原子化执行"检查+锁定"，避免竞态条件，只需一次 Redis 调用
        var result = await db.ScriptEvaluateAsync(
            TryLockScript,
            new RedisKey[] { key, lockKey },
            new RedisValue[]
            {
                DateTime.UtcNow.ToString("O"),
                (int)lockDuration.TotalSeconds
            });

        var lockAcquired = (int)result == 1;

        if (lockAcquired)
        {
            _logger.LogDebug("Acquired lock for message {MessageId}", messageId);
        }

        return lockAcquired;
    }

    /// <inheritdoc/>
    public async Task MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        var db = _redis.GetDatabase();
        var key = GetMessageKey(message.MessageId);
        var lockKey = GetLockKey(message.MessageId);

        message.ProcessedAt = DateTime.UtcNow;
        message.Status = InboxStatus.Processed;
        message.LockExpiresAt = null;

        var json = RedisJsonSerializer.Serialize(message);

        // 使用 Redis 事务保证原子性（无锁，依赖 Redis 原子性）
        var transaction = db.CreateTransaction();

        // 1. 保存处理结果（设置 TTL 自动过期）
        _ = transaction.StringSetAsync(key, json, TimeSpan.FromHours(24));

        // 2. 删除分布式锁
        _ = transaction.KeyDeleteAsync(lockKey);

        await transaction.ExecuteAsync();

        _logger.LogDebug("Marked message {MessageId} as processed", message.MessageId);
    }

    /// <inheritdoc/>
    public async Task<bool> HasBeenProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetMessageKey(messageId);

        // 单次 Redis 调用，无锁查询
        var json = await db.StringGetAsync(key);
        if (!json.HasValue)
            return false;

        var message = RedisJsonSerializer.Deserialize<InboxMessage>(json!);
        return message?.Status == InboxStatus.Processed;
    }

    /// <inheritdoc/>
    public async Task<string?> GetProcessedResultAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetMessageKey(messageId);

        // 单次 Redis 调用，无锁查询
        var json = await db.StringGetAsync(key);
        if (!json.HasValue)
            return null;

        var message = RedisJsonSerializer.Deserialize<InboxMessage>(json!);
        if (message?.Status == InboxStatus.Processed)
        {
            return message.ProcessingResult;
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task ReleaseLockAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var lockKey = GetLockKey(messageId);

        await db.KeyDeleteAsync(lockKey);

        _logger.LogDebug("Released lock for message {MessageId}", messageId);
    }

    /// <inheritdoc/>
    public async Task DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        // 由于已处理的消息设置了 24 小时过期时间，Redis 会自动删除
        // 这个方法主要用于一致性接口，实际清理由 Redis TTL 处理

        var db = _redis.GetDatabase();

        // 可以在这里实现额外的清理逻辑，比如扫描旧的锁
        // 由于 Redis 的 SCAN 是昂贵的操作，通常依赖 TTL 自动清理即可

        _logger.LogDebug("Redis inbox cleanup triggered (relying on TTL for actual cleanup)");

        await Task.CompletedTask;
    }

    private string GetMessageKey(string messageId) => $"{_keyPrefix}msg:{messageId}";
    private string GetLockKey(string messageId) => $"{_lockKeyPrefix}{messageId}";
}

