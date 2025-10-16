namespace Catga.Inbox;

/// <summary>
/// Inbox store for idempotent message processing (100% AOT compatible)
/// Ensures messages are processed exactly once
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Try to lock a message for processing (returns false if already processed or locked)
    /// </summary>
    public ValueTask<bool> TryLockMessageAsync(
        string messageId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark message as successfully processed
    /// </summary>
    public ValueTask MarkAsProcessedAsync(
        InboxMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if message has already been processed
    /// </summary>
    public ValueTask<bool> HasBeenProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get processing result for a message (if available)
    /// </summary>
    public ValueTask<string?> GetProcessedResultAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Release lock on a message (in case of processing failure)
    /// </summary>
    public ValueTask ReleaseLockAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete old processed messages (cleanup)
    /// </summary>
    public ValueTask DeleteProcessedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inbox message representation (100% AOT compatible)
/// </summary>
public class InboxMessage
{
    /// <summary>
    /// Unique message identifier
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Message type (full type name)
    /// </summary>
    public required string MessageType { get; set; }

    /// <summary>
    /// Serialized message payload (JSON)
    /// </summary>
    public required string Payload { get; set; }

    /// <summary>
    /// When the message was received
    /// </summary>
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the message was processed
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Processing result (JSON, if any)
    /// </summary>
    public string? ProcessingResult { get; set; }

    /// <summary>
    /// Message status
    /// </summary>
    public InboxStatus Status { get; set; } = InboxStatus.Pending;

    /// <summary>
    /// When the processing lock expires (if locked)
    /// </summary>
    public DateTime? LockExpiresAt { get; set; }

    /// <summary>
    /// Optional correlation ID for tracing
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Optional metadata (JSON)
    /// </summary>
    public string? Metadata { get; set; }
}

/// <summary>
/// Inbox message status
/// </summary>
public enum InboxStatus
{
    /// <summary>
    /// Waiting to be processed
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Currently being processed (locked)
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Successfully processed
    /// </summary>
    Processed = 2,

    /// <summary>
    /// Processing failed
    /// </summary>
    Failed = 3
}

