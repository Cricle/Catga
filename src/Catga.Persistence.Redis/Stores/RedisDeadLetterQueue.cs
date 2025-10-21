using System.Diagnostics.CodeAnalysis;
using System.Text;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>
/// Redis-based dead letter queue (lock-free, uses Redis List + Hash)
/// </summary>
/// <remarks>
/// Lock-free: Redis is single-threaded, all operations are atomic.
/// Uses Redis List for queue and Hash for message details.
/// </remarks>
public sealed class RedisDeadLetterQueue : IDeadLetterQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly string _listKey;
    private readonly string _hashKeyPrefix;

    public RedisDeadLetterQueue(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        string keyPrefix = "dlq:")
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _listKey = $"{keyPrefix}messages";
        _hashKeyPrefix = $"{keyPrefix}details:";
    }

    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        Exception exception,
        int retryCount,
        CancellationToken cancellationToken = default) where TMessage : IMessage
    {
        var db = _redis.GetDatabase();
        var messageId = message.MessageId.ToString();

        // Serialize message to JSON string
        var messageBytes = _serializer.Serialize(message);
        var messageJson = Encoding.UTF8.GetString(messageBytes);

        var dlqMessage = new DeadLetterMessage
        {
            MessageId = message.MessageId,
            MessageType = TypeNameCache<TMessage>.Name,
            MessageJson = messageJson,
            ExceptionType = ExceptionTypeCache.GetTypeName(exception),
            ExceptionMessage = exception.Message,
            StackTrace = exception.StackTrace ?? string.Empty,
            RetryCount = retryCount,
            FailedAt = DateTime.UtcNow
        };

        // Store details in Hash
        var hashKey = $"{_hashKeyPrefix}{messageId}";
        var hashEntries = new[]
        {
            new HashEntry("MessageType", dlqMessage.MessageType),
            new HashEntry("MessageJson", dlqMessage.MessageJson),
            new HashEntry("ExceptionType", dlqMessage.ExceptionType),
            new HashEntry("ExceptionMessage", dlqMessage.ExceptionMessage),
            new HashEntry("StackTrace", dlqMessage.StackTrace),
            new HashEntry("FailedAt", dlqMessage.FailedAt.Ticks),
            new HashEntry("RetryCount", dlqMessage.RetryCount)
        };

        await db.HashSetAsync(hashKey, hashEntries);
        await db.ListLeftPushAsync(_listKey, messageId);
    }

    public async Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var messageIds = await db.ListRangeAsync(_listKey, 0, maxCount - 1);

        var result = new List<DeadLetterMessage>(messageIds.Length);

        foreach (var messageId in messageIds)
        {
            var hashKey = $"{_hashKeyPrefix}{messageId}";
            var hash = await db.HashGetAllAsync(hashKey);

            if (hash.Length > 0)
            {
                var dict = hash.ToDictionary(h => h.Name.ToString(), h => h.Value);

                var dlqMessage = new DeadLetterMessage
                {
                    MessageId = long.Parse(messageId!),
                    MessageType = dict.GetValueOrDefault("MessageType").ToString(),
                    MessageJson = dict.GetValueOrDefault("MessageJson").ToString(),
                    ExceptionType = dict.GetValueOrDefault("ExceptionType").ToString(),
                    ExceptionMessage = dict.GetValueOrDefault("ExceptionMessage").ToString(),
                    StackTrace = dict.GetValueOrDefault("StackTrace").ToString(),
                    FailedAt = new DateTime((long)dict.GetValueOrDefault("FailedAt")),
                    RetryCount = (int)dict.GetValueOrDefault("RetryCount")
                };

                result.Add(dlqMessage);
            }
        }

        return result;
    }
}

