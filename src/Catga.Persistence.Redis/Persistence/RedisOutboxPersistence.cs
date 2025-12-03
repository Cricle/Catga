using System.Diagnostics;
using Catga.Abstractions;
using Catga.Observability;
using Catga.Outbox;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Persistence;

/// <summary>Redis Outbox persistence store.</summary>
public class RedisOutboxPersistence(IConnectionMultiplexer redis, IMessageSerializer serializer, ILogger<RedisOutboxPersistence> logger, IResiliencePipelineProvider provider, RedisOutboxOptions? options = null) : IOutboxStore
{
    private readonly string _prefix = options?.KeyPrefix ?? "outbox";
    private readonly string _pendingKey = $"{options?.KeyPrefix ?? "outbox"}:pending";
    private const string MarkPublishedScript = "redis.call('SET',KEYS[1],ARGV[2],'EX',ARGV[3]) redis.call('ZREM',KEYS[2],ARGV[1]) return 1";

    public async ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.Add", ActivityKind.Producer);
            if (message == null) throw new ArgumentNullException(nameof(message));

            var db = redis.GetDatabase();
            var key = GetMessageKey(message.MessageId);

            var data = serializer.Serialize(message, typeof(OutboxMessage));

            // 使用 Redis 事务保证原子性
            var transaction = db.CreateTransaction();

            // 1. 保存消息数据
            _ = transaction.StringSetAsync(key, data);

            // 2. 添加到待处理 SortedSet（按创建时间排序）
            var score = new DateTimeOffset(message.CreatedAt).ToUnixTimeSeconds();
            _ = transaction.SortedSetAddAsync(_pendingKey, message.MessageId, score);

            // 提交事务
            bool committed = await transaction.ExecuteAsync();

            if (committed)
            {
                CatgaLog.OutboxAdded(logger, message.MessageId);
                CatgaDiagnostics.OutboxAdded.Add(1);
                System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.OutboxAdded,
                    ("message.id", message.MessageId),
                    ("bytes", data.Length));
            }
            else
            {
                CatgaLog.OutboxAddFailed(logger, message.MessageId);
            }
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.GetPending", ActivityKind.Internal);
            var db = redis.GetDatabase();

            // 从 SortedSet 获取待处理消息 ID
            var messageIds = await db.SortedSetRangeByScoreAsync(
                _pendingKey,
                take: maxCount);

            if (messageIds.Length == 0)
            {
                System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.OutboxGetPendingEmpty);
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
                    var msg = (OutboxMessage?)serializer.Deserialize((byte[])values[i]!, typeof(OutboxMessage));
                    if (msg != null &&
                        msg.Status == OutboxStatus.Pending &&
                        msg.RetryCount < msg.MaxRetries)
                    {
                        messages.Add(msg);
                    }
                }
                catch (Exception ex)
                {
                    CatgaLog.OutboxDeserializeFailed(logger, ex, (long)messageIds[i]!);
                }
            }

            System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.OutboxGetPendingDone,
                ("count", messages.Count));
            return (IReadOnlyList<OutboxMessage>)messages;
        }, cancellationToken);
    }

    public async ValueTask MarkAsPublishedAsync(long messageId, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.MarkPublished", ActivityKind.Internal);
            var db = redis.GetDatabase();
            var key = GetMessageKey(messageId);

            // 获取消息
            var data = await db.StringGetAsync(key);
            if (!data.HasValue)
            {
                CatgaLog.OutboxMessageNotFound(logger, messageId);
                return;
            }

            var message = (OutboxMessage?)serializer.Deserialize((byte[])data!, typeof(OutboxMessage));
            if (message == null)
                return;

            // 更新状态
            message.Status = OutboxStatus.Published;
            message.PublishedAt = DateTime.UtcNow;

            // 使用 Lua 脚本原子化执行
            await db.ScriptEvaluateAsync(
                MarkPublishedScript,
                new RedisKey[] { key, _pendingKey },
                new RedisValue[]
                {
                    messageId,
                    serializer.Serialize(message, typeof(OutboxMessage)),
                    (int)TimeSpan.FromHours(24).TotalSeconds
                });

            CatgaLog.OutboxMarkedPublished(logger, messageId);
            CatgaDiagnostics.OutboxPublished.Add(1);
            System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.OutboxMarkPublished,
                ("message.id", messageId));
        }, cancellationToken);
    }

    public async ValueTask MarkAsFailedAsync(long messageId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.MarkFailed", ActivityKind.Internal);
            var db = redis.GetDatabase();
            var key = GetMessageKey(messageId);

            // 获取消息
            var data = await db.StringGetAsync(key);
            if (!data.HasValue)
                return;

            var message = serializer.Deserialize<OutboxMessage>(data!);
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
                _ = transaction.StringSetAsync(key, serializer.Serialize(message, typeof(OutboxMessage)));
                _ = transaction.SortedSetRemoveAsync(_pendingKey, messageId);
                await transaction.ExecuteAsync();

                CatgaLog.OutboxMessageFailedAfterRetries(logger, messageId, message.RetryCount, errorMessage);
                CatgaDiagnostics.OutboxFailed.Add(1);
                System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.OutboxMarkFailedFinal,
                    ("message.id", messageId),
                    ("retry", message.RetryCount));
            }
            else
            {
                // 还可以重试，保持在待处理集合中
                message.Status = OutboxStatus.Pending;
                await db.StringSetAsync(key, serializer.Serialize(message, typeof(OutboxMessage)));

                CatgaLog.OutboxMessageRetry(logger, messageId, message.RetryCount, message.MaxRetries, errorMessage);
                System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.OutboxMarkFailedRetry,
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
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.Cleanup", ActivityKind.Internal);
            var db = redis.GetDatabase();
            var cutoffScore = new DateTimeOffset(DateTime.UtcNow - retentionPeriod).ToUnixTimeSeconds();

            // 清理 SortedSet
            var removed = await db.SortedSetRemoveRangeByScoreAsync(
                _pendingKey,
                double.NegativeInfinity,
                cutoffScore);

            if (removed > 0)
            {
                CatgaLog.OutboxCleanup(logger, removed);
                System.Diagnostics.Activity.Current?.AddActivityEvent(CatgaActivitySource.Events.OutboxCleanup,
                    ("removed", removed));
            }
        }, cancellationToken);
    }

    private string GetMessageKey(long messageId) => $"{_prefix}:msg:{messageId}";
}



