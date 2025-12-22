using System.Diagnostics;
using Catga.Abstractions;
using Catga.Observability;
using Catga.Outbox;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Persistence;

/// <summary>Redis Outbox persistence store.</summary>
public class RedisOutboxPersistence(IConnectionMultiplexer redis, IMessageSerializer serializer, ILogger<RedisOutboxPersistence> logger, IResiliencePipelineProvider provider, IOptions<RedisPersistenceOptions>? options = null) : IOutboxStore
{
    private readonly string _prefix = options?.Value.OutboxKeyPrefix ?? "catga:outbox:";
    private readonly string _pendingKey = $"{options?.Value.OutboxKeyPrefix ?? "catga:outbox:"}pending";
    private const string MarkPublishedScript = "redis.call('SET',KEYS[1],ARGV[2],'EX',ARGV[3]) redis.call('ZREM',KEYS[2],ARGV[1]) return 1";

    public async ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.Add", ActivityKind.Producer);
            ArgumentNullException.ThrowIfNull(message);

            var db = redis.GetDatabase();
            var key = GetMessageKey(message.MessageId);
            var data = serializer.Serialize(message, typeof(OutboxMessage));

            var transaction = db.CreateTransaction();
            _ = transaction.StringSetAsync(key, data);
            var score = new DateTimeOffset(message.CreatedAt).ToUnixTimeSeconds();
            _ = transaction.SortedSetAddAsync(_pendingKey, message.MessageId, (double)score);

            if (await transaction.ExecuteAsync())
            {
                CatgaLog.OutboxAdded(logger, message.MessageId);
                CatgaDiagnostics.OutboxAdded.Add(1);
            }
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int maxCount = 100, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.GetPending", ActivityKind.Internal);
            var db = redis.GetDatabase();
            var messageIds = await db.SortedSetRangeByScoreAsync(_pendingKey, take: maxCount);
            if (messageIds.Length == 0) return (IReadOnlyList<OutboxMessage>)[];

            var messages = new List<OutboxMessage>(messageIds.Length);
            var keys = new RedisKey[messageIds.Length];
            for (int i = 0; i < messageIds.Length; i++) keys[i] = (RedisKey)GetMessageKey((long)messageIds[i]);

            var values = await db.StringGetAsync(keys);
            for (int i = 0; i < values.Length; i++)
            {
                if (!values[i].HasValue) continue;
                try
                {
                    var msg = (OutboxMessage?)serializer.Deserialize((byte[])values[i]!, typeof(OutboxMessage));
                    if (msg is { Status: OutboxStatus.Pending } && msg.RetryCount < msg.MaxRetries) messages.Add(msg);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to deserialize outbox message {MessageId}", (long)messageIds[i]!);
                }
            }
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
            var data = await db.StringGetAsync(key);
            if (!data.HasValue) return;

            var message = (OutboxMessage?)serializer.Deserialize((byte[])data!, typeof(OutboxMessage));
            if (message == null) return;

            message.Status = OutboxStatus.Published;
            message.PublishedAt = DateTime.UtcNow;

            await db.ScriptEvaluateAsync(MarkPublishedScript, [key, _pendingKey], [messageId, serializer.Serialize(message, typeof(OutboxMessage)), (RedisValue)(int)TimeSpan.FromHours(24).TotalSeconds]);
            CatgaLog.OutboxMarkedPublished(logger, messageId);
            CatgaDiagnostics.OutboxPublished.Add(1);
        }, cancellationToken);
    }

    public async ValueTask MarkAsFailedAsync(long messageId, string errorMessage, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.MarkFailed", ActivityKind.Internal);
            var db = redis.GetDatabase();
            var key = GetMessageKey(messageId);
            var data = await db.StringGetAsync(key);
            if (!data.HasValue) return;

            var message = serializer.Deserialize<OutboxMessage>(data!);
            if (message == null) return;

            message.RetryCount++;
            message.LastError = errorMessage;

            if (message.RetryCount >= message.MaxRetries)
            {
                message.Status = OutboxStatus.Failed;
                var transaction = db.CreateTransaction();
                _ = transaction.StringSetAsync(key, serializer.Serialize(message, typeof(OutboxMessage)));
                _ = transaction.SortedSetRemoveAsync(_pendingKey, messageId);
                await transaction.ExecuteAsync();
                CatgaLog.OutboxMessageFailedAfterRetries(logger, messageId, message.RetryCount);
                CatgaDiagnostics.OutboxFailed.Add(1);
            }
            else
            {
                message.Status = OutboxStatus.Pending;
                await db.StringSetAsync(key, serializer.Serialize(message, typeof(OutboxMessage)));
            }
        }, cancellationToken);
    }

    public async ValueTask DeletePublishedMessagesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.Outbox.Cleanup", ActivityKind.Internal);
            var db = redis.GetDatabase();
            var cutoffScore = new DateTimeOffset(DateTime.UtcNow - retentionPeriod).ToUnixTimeSeconds();
            await db.SortedSetRemoveRangeByScoreAsync(_pendingKey, double.NegativeInfinity, cutoffScore);
        }, cancellationToken);
    }

    private string GetMessageKey(long messageId) => $"{_prefix}msg:{messageId}";
}
