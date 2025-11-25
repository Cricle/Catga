using System;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using StackExchange.Redis;
using System.Diagnostics;
using Catga.Observability;

namespace Catga.Persistence.Redis;

/// <summary>
/// Redis-based dead letter queue (lock-free, uses Redis List + Hash)
/// </summary>
/// <remarks>
/// Lock-free: Redis is single-threaded, all operations are atomic.
/// Uses Redis List for queue and Hash for message details.
/// </remarks>
public sealed class RedisDeadLetterQueue : RedisStoreBase, IDeadLetterQueue
{
    private readonly string _listKey;
    private readonly string _hashKeyPrefix;

    public RedisDeadLetterQueue(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        string keyPrefix = "dlq:")
        : base(redis, serializer, keyPrefix)
    {
        _listKey = $"{keyPrefix}messages";
        _hashKeyPrefix = $"{keyPrefix}details:";
    }

    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        Exception exception,
        int retryCount,
        CancellationToken cancellationToken = default) where TMessage : IMessage
    {
        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.DeadLetter.Send", ActivityKind.Producer);
        var db = GetDatabase();
        var messageId = message.MessageId.ToString();

        // Serialize message to Base64 string (serializer-agnostic)
        var messageJson = Convert.ToBase64String(Serializer.Serialize(message, typeof(TMessage)));

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
        CatgaDiagnostics.DeadLetters.Add(1);
    }

    public async Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.Redis.DeadLetter.GetFailed", ActivityKind.Internal);
        var db = GetDatabase();
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

