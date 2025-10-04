using Catga.Messages;

namespace Catga.DeadLetter;

/// <summary>
/// Dead letter queue for failed messages (AOT-compatible)
/// </summary>
public interface IDeadLetterQueue
{
    /// <summary>
    /// Send message to dead letter queue
    /// </summary>
    Task SendAsync<TMessage>(
        TMessage message,
        Exception exception,
        int retryCount,
        CancellationToken cancellationToken = default)
        where TMessage : IMessage;

    /// <summary>
    /// Get failed messages for inspection
    /// </summary>
    Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Dead letter message envelope
/// </summary>
public class DeadLetterMessage
{
    public required string MessageId { get; init; }
    public required string MessageType { get; init; }
    public required string MessageJson { get; init; }
    public required string ExceptionType { get; init; }
    public required string ExceptionMessage { get; init; }
    public required string StackTrace { get; init; }
    public int RetryCount { get; init; }
    public DateTime FailedAt { get; init; }
}

