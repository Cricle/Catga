using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.Observability;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>Options for RedisDeadLetterQueue.</summary>
public class RedisDeadLetterQueueOptions
{
    /// <summary>Key prefix for DLQ entries. Default: dlq:</summary>
    public string KeyPrefix { get; set; } = "dlq:";
}

/// <summary>Redis-based dead letter queue.</summary>
public sealed class RedisDeadLetterQueue : RedisStoreBase, IDeadLetterQueue
{
    private readonly string _listKey;
    private readonly string _hashPrefix;

    public RedisDeadLetterQueue(IConnectionMultiplexer redis, IMessageSerializer serializer, IOptions<RedisDeadLetterQueueOptions>? options = null)
        : base(redis, serializer, options?.Value.KeyPrefix ?? "dlq:")
    {
        var prefix = options?.Value.KeyPrefix ?? "dlq:";
        _listKey = $"{prefix}messages";
        _hashPrefix = $"{prefix}details:";
    }

    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, Exception ex, int retryCount, CancellationToken ct = default) where TMessage : IMessage
    {
        using var _ = CatgaDiagnostics.ActivitySource.StartActivity("Redis.DLQ.Send", ActivityKind.Producer);
        var db = GetDatabase();
        var id = message.MessageId.ToString();
        var messageBytes = Serializer.Serialize(message);
        var dlm = new DeadLetterMessage { MessageId = message.MessageId, MessageType = TypeNameCache<TMessage>.Name, Message = messageBytes, ExceptionType = ex.GetType().Name, ExceptionMessage = ex.Message, StackTrace = ex.StackTrace ?? "", RetryCount = retryCount, FailedAt = DateTime.UtcNow };
        await db.HashSetAsync($"{_hashPrefix}{id}", [
            new HashEntry("MessageType", dlm.MessageType),
            new HashEntry("MessageData", messageBytes),
            new HashEntry("ExceptionType", dlm.ExceptionType),
            new HashEntry("ExceptionMessage", dlm.ExceptionMessage),
            new HashEntry("StackTrace", dlm.StackTrace),
            new HashEntry("FailedAt", (RedisValue)dlm.FailedAt.Ticks),
            new HashEntry("RetryCount", (RedisValue)dlm.RetryCount)
        ]);
        await db.ListLeftPushAsync(_listKey, id);
        CatgaDiagnostics.DeadLetters.Add(1);
    }

    public async Task<List<DeadLetterMessage>> GetFailedMessagesAsync(int maxCount = 100, CancellationToken ct = default)
    {
        using var _ = CatgaDiagnostics.ActivitySource.StartActivity("Redis.DLQ.GetFailed", ActivityKind.Internal);
        var db = GetDatabase();
        var ids = await db.ListRangeAsync(_listKey, 0, maxCount - 1);
        var result = new List<DeadLetterMessage>(ids.Length);
        foreach (var id in ids)
        {
            var h = await db.HashGetAllAsync($"{_hashPrefix}{id}");
            if (h.Length == 0) continue;
            var d = h.ToDictionary(x => x.Name.ToString(), x => x.Value);
            result.Add(new() { MessageId = long.Parse(id!), MessageType = d.GetValueOrDefault("MessageType").ToString(), Message = (byte[])d.GetValueOrDefault("MessageData")!, ExceptionType = d.GetValueOrDefault("ExceptionType").ToString(), ExceptionMessage = d.GetValueOrDefault("ExceptionMessage").ToString(), StackTrace = d.GetValueOrDefault("StackTrace").ToString(), FailedAt = new((long)d.GetValueOrDefault("FailedAt")), RetryCount = (int)d.GetValueOrDefault("RetryCount") });
        }
        return result;
    }
}

