using Catga.Abstractions;
using Catga.Outbox;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Catga.Resilience;
using Catga.Persistence.Redis;
using System.Diagnostics;
using Catga.Observability;

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
    private readonly IResiliencePipelineProvider _provider;

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
        RedisOutboxOptions? options = null,
        IResiliencePipelineProvider? provider = null)
    {
        _redis = redis;
        _serializer = serializer;
        _logger = logger;
        _keyPrefix = options?.KeyPrefix ?? "outbox";
        _pendingSetKey = $"{_keyPrefix}:pending";
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.Add", ActivityKind.Producer);
            if (message == null) throw new ArgumentNullException(nameof(message));

            var db = _redis.GetDatabase();
            var key = GetMessageKey(message.MessageId);

            var data = _serializer.Serialize(message, typeof(OutboxMessage));

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
                CatgaLog.OutboxAdded(_logger, message.MessageId);
                CatgaDiagnostics.OutboxAdded.Add(1);
                System.Diagnostics.Activity.Current?.AddActivityEvent("Outbox.Added",
                    ("message.id", message.MessageId),
                    ("bytes", data.Length));
            }
            else
            {
                CatgaLog.OutboxAddFailed(_logger, message.MessageId);
            }
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.GetPending", ActivityKind.Internal);
            var db = _redis.GetDatabase();

            // 从 SortedSet 获取待处理消息 ID
            var messageIds = await db.SortedSetRangeByScoreAsync(
                _pendingSetKey,
                take: maxCount);

            if (messageIds.Length == 0)
            {
                System.Diagnostics.Activity.Current?.AddActivityEvent("Outbox.GetPending.Empty");
                return (IReadOnlyList<OutboxMessage>)Array.Empty<OutboxMessage>();
            }

            var messages = new List<OutboxMessage>(messageIds.Length);

            // 批量 GET 操作 - Build keys array
            var keys = new RedisKey[messageIds.Length];
            for (int i = 0; i < messageIds.Length; i++)
            {
                keys[i] = (RedisKey)GetMessageKey((long)messageIds[i]);
            }

            var values = await db.StringGetAsync(keys);

            // 反序列化和过滤
            for (int i = 0; i < values.Length; i++)
            {
                if (!values[i].HasValue)
                    continue;

                try
                {
                    var msg = (OutboxMessage?)_serializer.Deserialize((byte[])values[i]!, typeof(OutboxMessage));
                    if (msg != null &&
                        msg.Status == OutboxStatus.Pending &&
                        msg.RetryCount < msg.MaxRetries)
                    {
                        messages.Add(msg);
                    }
                }
                catch (Exception ex)
                {
                    CatgaLog.OutboxDeserializeFailed(_logger, ex, (long)messageIds[i]!);
                }
            }

            System.Diagnostics.Activity.Current?.AddActivityEvent("Outbox.GetPending.Done",
                ("count", messages.Count));
            return (IReadOnlyList<OutboxMessage>)messages;
        }, cancellationToken);
    }

    public async ValueTask MarkAsPublishedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.MarkPublished", ActivityKind.Internal);
            var db = _redis.GetDatabase();
            var key = GetMessageKey(messageId);

            // 获取消息
            var data = await db.StringGetAsync(key);
            if (!data.HasValue)
            {
                CatgaLog.OutboxMessageNotFound(_logger, messageId);
                return;
            }

            var message = (OutboxMessage?)_serializer.Deserialize((byte[])data!, typeof(OutboxMessage));
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
                    _serializer.Serialize(message, typeof(OutboxMessage)),
                    (int)TimeSpan.FromHours(24).TotalSeconds
                });

            CatgaLog.OutboxMarkedPublished(_logger, messageId);
            CatgaDiagnostics.OutboxPublished.Add(1);
            System.Diagnostics.Activity.Current?.AddActivityEvent("Outbox.MarkPublished",
                ("message.id", messageId));
        }, cancellationToken);
    }

    public async ValueTask MarkAsFailedAsync(long messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.MarkFailed", ActivityKind.Internal);
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
                _ = transaction.StringSetAsync(key, _serializer.Serialize(message, typeof(OutboxMessage)));
                _ = transaction.SortedSetRemoveAsync(_pendingSetKey, messageId);
                await transaction.ExecuteAsync();

                CatgaLog.OutboxMessageFailedAfterRetries(_logger, messageId, message.RetryCount, errorMessage);
                CatgaDiagnostics.OutboxFailed.Add(1);
                System.Diagnostics.Activity.Current?.AddActivityEvent("Outbox.MarkFailed.Final",
                    ("message.id", messageId),
                    ("retry", message.RetryCount));
            }
            else
            {
                // 还可以重试，保持在待处理集合中
                message.Status = OutboxStatus.Pending;
                await db.StringSetAsync(key, _serializer.Serialize(message, typeof(OutboxMessage)));

                CatgaLog.OutboxMessageRetry(_logger, messageId, message.RetryCount, message.MaxRetries, errorMessage);
                System.Diagnostics.Activity.Current?.AddActivityEvent("Outbox.MarkFailed.Retry",
                    ("message.id", messageId),
                    ("retry", message.RetryCount),
                    ("max", message.MaxRetries));
            }
        }, cancellationToken);
    }
    public async ValueTask DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        await _provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.Cleanup", ActivityKind.Internal);
            var db = _redis.GetDatabase();
            var cutoffScore = new DateTimeOffset(DateTime.UtcNow - retentionPeriod).ToUnixTimeSeconds();

            // 清理 SortedSet
            var removed = await db.SortedSetRemoveRangeByScoreAsync(
                _pendingSetKey,
                double.NegativeInfinity,
                cutoffScore);

            if (removed > 0)
            {
                CatgaLog.OutboxCleanup(_logger, removed);
                System.Diagnostics.Activity.Current?.AddActivityEvent("Outbox.Cleanup",
                    ("removed", removed));
            }
        }, cancellationToken);
    }

    private string GetMessageKey(long messageId) => $"{_keyPrefix}:msg:{messageId}";
}



