using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catga.Messages;
using Microsoft.Extensions.Logging;

namespace Catga.DeadLetter;

/// <summary>
/// Streamlined in-memory dead letter queue (lock-free)
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

    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation.")]
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

        // Simple trimming strategy
        while (_deadLetters.Count > _maxSize)
            _deadLetters.TryDequeue(out _);

        _logger.LogWarning("Message sent to dead letter queue: {MessageType} {MessageId}, retries: {RetryCount}",
            deadLetter.MessageType, deadLetter.MessageId, retryCount);

        return Task.CompletedTask;
    }

    public Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
    {
        // Zero-allocation optimization: avoid LINQ, build list directly
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

