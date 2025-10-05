using Catga.Outbox;
using Catga.Redis.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Redis;

/// <summary>
/// Redis outbox store implementation - Lock-free optimized
/// </summary>
public class RedisOutboxStore : IOutboxStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisOutboxStore> _logger;
    private readonly string _keyPrefix;
    private readonly string _pendingSetKey;

    // Lua 脚本：原子化更新消息状态（避免读-修改-写竞态）
    private const string MarkAsPublishedScript = @"
        local msgKey = KEYS[1]
        local pendingSet = KEYS[2]
        local messageId = ARGV[1]
        local updatedMsg = ARGV[2]
        local ttl = tonumber(ARGV[3])

        -- 原子化更新消息、移除待处理集合、设置过期
        redis.call('SET', msgKey, updatedMsg, 'EX', ttl)
        redis.call('ZREM', pendingSet, messageId)

        return 1
    ";

    public RedisOutboxStore(
        IConnectionMultiplexer redis,
        ILogger<RedisOutboxStore> logger,
        RedisCatgaOptions? options = null)
    {
        _redis = redis;
        _logger = logger;
        _keyPrefix = options?.OutboxKeyPrefix ?? "outbox:";
        _pendingSetKey = $"{_keyPrefix}pending";
    }

    /// <inheritdoc/>
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        var db = _redis.GetDatabase();
        var key = GetMessageKey(message.MessageId);

        // 序列化消息
        var json = RedisJsonSerializer.Serialize(message);

        // 使用 Redis 事务保证原子性（无锁，依赖 Redis 原子性）
        var transaction = db.CreateTransaction();

        // 1. 保存消息数据
        _ = transaction.StringSetAsync(key, json);

        // 2. 添加到待处理 SortedSet（按创建时间排序，支持高效范围查询）
        var score = new DateTimeOffset(message.CreatedAt).ToUnixTimeSeconds();
        _ = transaction.SortedSetAddAsync(_pendingSetKey, message.MessageId, score);

        // 提交事务（原子化提交，无需额外锁）
        bool committed = await transaction.ExecuteAsync();

        if (committed)
        {
            _logger.LogDebug("Added message {MessageId} to outbox", message.MessageId);
        }
        else
        {
            _logger.LogWarning("Failed to add message {MessageId} to outbox (transaction failed)", message.MessageId);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        // 从 SortedSet 获取待处理消息 ID（按时间排序，无锁查询）
        var messageIds = await db.SortedSetRangeByScoreAsync(
            _pendingSetKey,
            take: maxCount);

        if (messageIds.Length == 0)
            return Array.Empty<OutboxMessage>();

        var messages = new List<OutboxMessage>(messageIds.Length);

        // 使用批量 GET 操作（单次网络往返获取多个 key，提高性能）
        var keys = messageIds.Select(id => (RedisKey)GetMessageKey(id.ToString())).ToArray();
        var values = await db.StringGetAsync(keys);

        // 本地过滤和解析（无需额外 Redis 调用）
        for (int i = 0; i < values.Length; i++)
        {
            if (!values[i].HasValue)
                continue;

            try
            {
                var message = RedisJsonSerializer.Deserialize<OutboxMessage>(values[i]!);
                if (message != null &&
                    message.Status == OutboxStatus.Pending &&
                    message.RetryCount < message.MaxRetries)
                {
                    messages.Add(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize outbox message {MessageId}", messageIds[i]);
            }
        }

        return messages;
    }

    /// <inheritdoc/>
    public async Task MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetMessageKey(messageId);

        // 获取消息（单次查询）
        var json = await db.StringGetAsync(key);
        if (!json.HasValue)
        {
            _logger.LogWarning("Message {MessageId} not found in outbox", messageId);
            return;
        }

        var message = RedisJsonSerializer.Deserialize<OutboxMessage>(json!);
        if (message == null)
            return;

        // 更新状态（本地修改，无 Redis 调用）
        message.Status = OutboxStatus.Published;
        message.PublishedAt = DateTime.UtcNow;

        // 使用 Lua 脚本原子化执行"更新+移除+设置TTL"（单次 Redis 调用）
        await db.ScriptEvaluateAsync(
            MarkAsPublishedScript,
            new RedisKey[] { key, _pendingSetKey },
            new RedisValue[]
            {
                messageId,
                RedisJsonSerializer.Serialize(message),
                (int)TimeSpan.FromHours(24).TotalSeconds
            });

        _logger.LogDebug("Marked message {MessageId} as published", messageId);
    }

    /// <inheritdoc/>
    public async Task MarkAsFailedAsync(
        string messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetMessageKey(messageId);

        // 单次查询获取消息
        var json = await db.StringGetAsync(key);
        if (!json.HasValue)
            return;

        var message = RedisJsonSerializer.Deserialize<OutboxMessage>(json!);
        if (message == null)
            return;

        // 增加重试计数（本地修改）
        message.RetryCount++;
        message.LastError = errorMessage;

        // 如果超过最大重试次数，标记为失败
        if (message.RetryCount >= message.MaxRetries)
        {
            message.Status = OutboxStatus.Failed;

            // 使用 Redis 事务原子化更新（无锁）
            var transaction = db.CreateTransaction();
            _ = transaction.StringSetAsync(key, RedisJsonSerializer.Serialize(message));
            _ = transaction.SortedSetRemoveAsync(_pendingSetKey, messageId);
            await transaction.ExecuteAsync();

            _logger.LogWarning("Message {MessageId} failed after {RetryCount} retries: {Error}",
                messageId, message.RetryCount, errorMessage);
        }
        else
        {
            // 还可以重试，保持在待处理集合中（单次写入）
            message.Status = OutboxStatus.Pending;
            await db.StringSetAsync(key, RedisJsonSerializer.Serialize(message));

            _logger.LogDebug("Message {MessageId} failed (retry {RetryCount}/{MaxRetries}): {Error}",
                messageId, message.RetryCount, message.MaxRetries, errorMessage);
        }
    }

    /// <inheritdoc/>
    public async Task DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var cutoffScore = new DateTimeOffset(DateTime.UtcNow - retentionPeriod).ToUnixTimeSeconds();

        // 使用 Redis 原子操作清理 SortedSet（无锁，单次调用）
        // 已发布的消息由 TTL 自动清理，这里只清理 SortedSet 残留
        var removed = await db.SortedSetRemoveRangeByScoreAsync(
            _pendingSetKey,
            double.NegativeInfinity,
            cutoffScore);

        if (removed > 0)
        {
            _logger.LogInformation("Cleaned up {Count} old outbox entries from SortedSet", removed);
        }
    }

    private string GetMessageKey(string messageId) => $"{_keyPrefix}msg:{messageId}";
}

