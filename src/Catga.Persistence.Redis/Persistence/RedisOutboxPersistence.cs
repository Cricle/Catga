using Catga.Outbox;
using Catga.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Persistence.Redis.Persistence;

/// <summary>
/// Redis Outbox 持久化存储 - 专注于存储，不涉及传输
/// </summary>
public class RedisOutboxPersistence : IOutboxStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<RedisOutboxPersistence> _logger;
    private readonly string _keyPrefix;
    private readonly string _pendingSetKey;

    // Lua 脚本：原子化更新消息状态
    private const string MarkAsPublishedScript = @"
        local msgKey = KEYS[1]
        local pendingSet = KEYS[2]
        local messageId = ARGV[1]
        local updatedMsg = ARGV[2]
        local ttl = tonumber(ARGV[3])

        redis.call('SET', msgKey, updatedMsg, 'EX', ttl)
        redis.call('ZREM', pendingSet, messageId)

        return 1
    ";

    public RedisOutboxPersistence(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        ILogger<RedisOutboxPersistence> logger,
        RedisOutboxOptions? options = null)
    {
        _redis = redis;
        _serializer = serializer;
        _logger = logger;
        _keyPrefix = options?.KeyPrefix ?? "outbox";
        _pendingSetKey = $"{_keyPrefix}:pending";
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        var db = _redis.GetDatabase();
        var key = GetMessageKey(message.MessageId);

        // 序列化消息
        var data = _serializer.Serialize(message);

        // 使用 Redis 事务保证原子性
        var transaction = db.CreateTransaction();

        // 1. 保存消息数据
        _ = transaction.StringSetAsync(key, data);

        // 2. 添加到待处理 SortedSet（按创建时间排序）
        var score = new DateTimeOffset(message.CreatedAt).ToUnixTimeSeconds();
        _ = transaction.SortedSetAddAsync(_pendingSetKey, message.MessageId, score);

        // 提交事务
        bool committed = await transaction.ExecuteAsync();

        if (committed)
        {
            _logger.LogDebug("Added message {MessageId} to outbox persistence", message.MessageId);
        }
        else
        {
            _logger.LogWarning("Failed to add message {MessageId} to outbox (transaction failed)", message.MessageId);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        // 从 SortedSet 获取待处理消息 ID
        var messageIds = await db.SortedSetRangeByScoreAsync(
            _pendingSetKey,
            take: maxCount);

        if (messageIds.Length == 0)
            return Array.Empty<OutboxMessage>();

        var messages = new List<OutboxMessage>(messageIds.Length);

        // 批量 GET 操作
        var keys = messageIds.Select(id => (RedisKey)GetMessageKey(id.ToString())).ToArray();
        var values = await db.StringGetAsync(keys);

        // 反序列化和过滤
        for (int i = 0; i < values.Length; i++)
        {
            if (!values[i].HasValue)
                continue;

            try
            {
                var message = _serializer.Deserialize<OutboxMessage>(values[i]!);
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public async Task MarkAsPublishedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetMessageKey(messageId);

        // 获取消息
        var data = await db.StringGetAsync(key);
        if (!data.HasValue)
        {
            _logger.LogWarning("Message {MessageId} not found in outbox", messageId);
            return;
        }

        var message = _serializer.Deserialize<OutboxMessage>(data!);
        if (message == null)
            return;

        // 更新状态
        message.Status = OutboxStatus.Published;
        message.PublishedAt = DateTime.UtcNow;

        // 使用 Lua 脚本原子化执行
        await db.ScriptEvaluateAsync(
            MarkAsPublishedScript,
            new RedisKey[] { key, _pendingSetKey },
            new RedisValue[]
            {
                messageId,
                _serializer.Serialize(message),
                (int)TimeSpan.FromHours(24).TotalSeconds
            });

        _logger.LogDebug("Marked message {MessageId} as published", messageId);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "序列化警告已在 IMessageSerializer 接口上标记")]
    public async Task MarkAsFailedAsync(
        string messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = GetMessageKey(messageId);

        // 获取消息
        var data = await db.StringGetAsync(key);
        if (!data.HasValue)
            return;

        var message = _serializer.Deserialize<OutboxMessage>(data!);
        if (message == null)
            return;

        // 增加重试计数
        message.RetryCount++;
        message.LastError = errorMessage;

        // 如果超过最大重试次数，标记为失败
        if (message.RetryCount >= message.MaxRetries)
        {
            message.Status = OutboxStatus.Failed;

            var transaction = db.CreateTransaction();
            _ = transaction.StringSetAsync(key, _serializer.Serialize(message));
            _ = transaction.SortedSetRemoveAsync(_pendingSetKey, messageId);
            await transaction.ExecuteAsync();

            _logger.LogWarning("Message {MessageId} failed after {RetryCount} retries: {Error}",
                messageId, message.RetryCount, errorMessage);
        }
        else
        {
            // 还可以重试，保持在待处理集合中
            message.Status = OutboxStatus.Pending;
            await db.StringSetAsync(key, _serializer.Serialize(message));

            _logger.LogDebug("Message {MessageId} failed (retry {RetryCount}/{MaxRetries}): {Error}",
                messageId, message.RetryCount, message.MaxRetries, errorMessage);
        }
    }

    public async Task DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var cutoffScore = new DateTimeOffset(DateTime.UtcNow - retentionPeriod).ToUnixTimeSeconds();

        // 清理 SortedSet
        var removed = await db.SortedSetRemoveRangeByScoreAsync(
            _pendingSetKey,
            double.NegativeInfinity,
            cutoffScore);

        if (removed > 0)
        {
            _logger.LogInformation("Cleaned up {Count} old outbox entries", removed);
        }
    }

    private string GetMessageKey(string messageId) => $"{_keyPrefix}:msg:{messageId}";
}

/// <summary>
/// Redis Outbox 持久化选项
/// </summary>
public class RedisOutboxOptions
{
    public string KeyPrefix { get; set; } = "outbox";
}

