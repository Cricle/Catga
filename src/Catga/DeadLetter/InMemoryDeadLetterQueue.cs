using System.Collections.Concurrent;
using System.Text.Json;
using Catga.Messages;
using Microsoft.Extensions.Logging;

namespace Catga.DeadLetter;

/// <summary>
/// Simple in-memory dead letter queue (lock-free, AOT-compatible)
/// </summary>
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

    public Task SendAsync<TMessage>(
        TMessage message,
        Exception exception,
        int retryCount,
        CancellationToken cancellationToken = default)
        where TMessage : IMessage
    {
        var deadLetter = new DeadLetterMessage
        {
            MessageId = message.MessageId,
            MessageType = typeof(TMessage).Name,
            MessageJson = JsonSerializer.Serialize(message),
            ExceptionType = exception.GetType().Name,
            ExceptionMessage = exception.Message,
            StackTrace = exception.StackTrace ?? string.Empty,
            RetryCount = retryCount,
            FailedAt = DateTime.UtcNow
        };

        _deadLetters.Enqueue(deadLetter);

        // Trim if too large (simple approach)
        while (_deadLetters.Count > _maxSize)
        {
            _deadLetters.TryDequeue(out _);
        }

        _logger.LogWarning(
            "Message sent to DLQ: {MessageType} {MessageId}, Retry: {RetryCount}, Error: {Error}",
            deadLetter.MessageType, deadLetter.MessageId, retryCount, exception.Message);

        return Task.CompletedTask;
    }

    public Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        var messages = _deadLetters
            .Take(maxCount)
            .ToList();

        return Task.FromResult(messages);
    }
}

