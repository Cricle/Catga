using System.Collections.Concurrent;
using Catga.Common;
using Catga.Core;
using Catga.Messages;
using Microsoft.Extensions.Logging;

namespace Catga.DeadLetter;

/// <summary>In-memory dead letter queue (lock-free)</summary>
public class InMemoryDeadLetterQueue : IDeadLetterQueue
{
    private readonly ConcurrentQueue<DeadLetterMessage> _deadLetters = new();
    private readonly ILogger<InMemoryDeadLetterQueue> _logger;
    private readonly int _maxSize;

    public InMemoryDeadLetterQueue(ILogger<InMemoryDeadLetterQueue> logger, int maxSize = 1000)
    {
        _logger = logger;
        _maxSize = maxSize;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "InMemory store is for development/testing. Use Redis for production AOT.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "InMemory store is for development/testing. Use Redis for production AOT.")]
    public Task SendAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, Exception exception, int retryCount, CancellationToken cancellationToken = default) where TMessage : IMessage
    {
        var deadLetter = new DeadLetterMessage
        {
            MessageId = message.MessageId,
            MessageType = TypeNameCache<TMessage>.Name,
            MessageJson = SerializationHelper.SerializeJson(message),
            ExceptionType = ExceptionTypeCache.GetTypeName(exception),
            ExceptionMessage = exception.Message,
            StackTrace = exception.StackTrace ?? string.Empty,
            RetryCount = retryCount,
            FailedAt = DateTime.UtcNow
        };

        _deadLetters.Enqueue(deadLetter);

        while (_deadLetters.Count > _maxSize)
            _deadLetters.TryDequeue(out _);

        _logger.LogWarning("Message sent to dead letter queue: {MessageType} {MessageId}, retries: {RetryCount}", deadLetter.MessageType, deadLetter.MessageId, retryCount);

        return Task.CompletedTask;
    }

    public Task<List<DeadLetterMessage>> GetFailedMessagesAsync(int maxCount = 100, CancellationToken cancellationToken = default)
    {
        var result = new List<DeadLetterMessage>(Math.Min(maxCount, _deadLetters.Count));
        var count = 0;
        foreach (var item in _deadLetters)
        {
            if (count >= maxCount) break;
            result.Add(item);
            count++;
        }
        return Task.FromResult(result);
    }
}

