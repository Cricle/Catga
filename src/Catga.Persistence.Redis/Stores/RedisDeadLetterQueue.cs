using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.Observability;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>Redis-based dead letter queue.</summary>
public sealed class RedisDeadLetterQueue(IConnectionMultiplexer redis, IMessageSerializer serializer, string keyPrefix = "dlq:") : RedisStoreBase(redis, serializer, keyPrefix), IDeadLetterQueue
{
    private readonly string _listKey = $"{keyPrefix}messages";
    private readonly string _hashPrefix = $"{keyPrefix}details:";

    public async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, Exception ex, int retryCount, CancellationToken ct = default) where TMessage : IMessage
    {
        using var _ = CatgaDiagnostics.ActivitySource.StartActivity("Redis.DLQ.Send", ActivityKind.Producer);
        var db = GetDatabase();
        var id = message.MessageId.ToString();
        var dlm = new DeadLetterMessage { MessageId = message.MessageId, MessageType = TypeNameCache<TMessage>.Name, MessageJson = Convert.ToBase64String(Serializer.Serialize(message, typeof(TMessage))), ExceptionType = ex.GetType().Name, ExceptionMessage = ex.Message, StackTrace = ex.StackTrace ?? "", RetryCount = retryCount, FailedAt = DateTime.UtcNow };
        await db.HashSetAsync($"{_hashPrefix}{id}", [new("MessageType", dlm.MessageType), new("MessageJson", dlm.MessageJson), new("ExceptionType", dlm.ExceptionType), new("ExceptionMessage", dlm.ExceptionMessage), new("StackTrace", dlm.StackTrace), new("FailedAt", dlm.FailedAt.Ticks), new("RetryCount", dlm.RetryCount)]);
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
            result.Add(new() { MessageId = long.Parse(id!), MessageType = d.GetValueOrDefault("MessageType").ToString(), MessageJson = d.GetValueOrDefault("MessageJson").ToString(), ExceptionType = d.GetValueOrDefault("ExceptionType").ToString(), ExceptionMessage = d.GetValueOrDefault("ExceptionMessage").ToString(), StackTrace = d.GetValueOrDefault("StackTrace").ToString(), FailedAt = new((long)d.GetValueOrDefault("FailedAt")), RetryCount = (int)d.GetValueOrDefault("RetryCount") });
        }
        return result;
    }
}

