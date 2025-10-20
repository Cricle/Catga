using System.Collections.Concurrent;
using Catga.Abstractions;
using Catga.Core;
using Catga.Core;
using Microsoft.Extensions.Logging;

namespace Catga.DeadLetter;

/// <summary>In-memory dead letter queue (lock-free, ArrayPool optimized)</summary>
public class InMemoryDeadLetterQueue : IDeadLetterQueue
{
    private readonly ConcurrentQueue<DeadLetterMessage> _deadLetters = new();
    private readonly ILogger<InMemoryDeadLetterQueue> _logger;
    private readonly IMessageSerializer _serializer;
    private readonly int _maxSize;

    public InMemoryDeadLetterQueue(
        ILogger<InMemoryDeadLetterQueue> logger,
        IMessageSerializer serializer,
        int maxSize = 1000)
    {
        _logger = logger;
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _maxSize = maxSize;
    }

    public Task SendAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, Exception exception, int retryCount, CancellationToken cancellationToken = default) where TMessage : IMessage
    {
        var messageBytes = _serializer.Serialize(message);
        var messageJson = System.Text.Encoding.UTF8.GetString(messageBytes);

        var deadLetter = new DeadLetterMessage
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

