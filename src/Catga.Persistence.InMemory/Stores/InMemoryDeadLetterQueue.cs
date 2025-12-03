using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Microsoft.Extensions.Logging;

namespace Catga.DeadLetter;

/// <summary>In-memory dead letter queue.</summary>
public class InMemoryDeadLetterQueue(ILogger<InMemoryDeadLetterQueue> logger, IMessageSerializer serializer, int maxSize = 1000) : IDeadLetterQueue
{
    private readonly ConcurrentQueue<DeadLetterMessage> _queue = new();

    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message, Exception exception, int retryCount, CancellationToken ct = default) where TMessage : IMessage
    {
        var dlm = new DeadLetterMessage
        {
            MessageId = message.MessageId,
            MessageType = TypeNameCache<TMessage>.Name,
            MessageJson = Convert.ToBase64String(serializer.Serialize(message, typeof(TMessage))),
            ExceptionType = exception.GetType().Name,
            ExceptionMessage = exception.Message,
            StackTrace = exception.StackTrace ?? "",
            RetryCount = retryCount,
            FailedAt = DateTime.UtcNow
        };
        _queue.Enqueue(dlm);
        CatgaDiagnostics.DeadLetters.Add(1);
        while (_queue.Count > maxSize) _queue.TryDequeue(out _);
        CatgaLog.MessageMovedToDLQ(logger, dlm.MessageType, dlm.MessageId, dlm.ExceptionMessage, retryCount);
        return Task.CompletedTask;
    }

    public Task<List<DeadLetterMessage>> GetFailedMessagesAsync(int maxCount = 100, CancellationToken ct = default)
        => Task.FromResult(_queue.Take(maxCount).ToList());
}

