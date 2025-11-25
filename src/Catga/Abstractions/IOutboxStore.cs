using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using MemoryPack;

namespace Catga.Outbox;

/// <summary>
/// Outbox store for reliable message publishing
/// Note: Some implementations may require dynamic code/reflection for serialization
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Add a message to the outbox (within the same transaction)
    /// </summary>
    public ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending messages ready to be published
    /// </summary>
    public ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark message as published successfully
    /// </summary>
    public ValueTask MarkAsPublishedAsync(long messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark message as failed and increment retry count
    /// </summary>
    public ValueTask MarkAsFailedAsync(
        long messageId,
        string errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete old published messages (cleanup)
    /// </summary>
    public ValueTask DeletePublishedMessagesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outbox message representation (100% AOT compatible)
/// </summary>
[MemoryPackable]
public partial class OutboxMessage
{
    /// <summary>
    /// Unique message identifier (Snowflake ID)
    /// </summary>
    public required long MessageId { get; init; }

    /// <summary>
    /// Message type (full type name for routing)
    /// </summary>
    public required string MessageType { get; init; }

    /// <summary>
    /// Serialized message payload (Base64-encoded, format depends on registered IMessageSerializer)
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    /// When the message was created
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the message was published (null if not yet published)
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// Number of publish attempts
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Maximum retry attempts before moving to dead letter
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Last error message (if failed)
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Message status
    /// </summary>
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>
    /// Optional correlation ID for tracing (Snowflake ID)
    /// </summary>
    public long? CorrelationId { get; init; }

    /// <summary>
    /// Optional metadata (JSON)
    /// </summary>
    public string? Metadata { get; init; }
}

/// <summary>
/// Outbox message status
/// </summary>
public enum OutboxStatus
{
    /// <summary>
    /// Waiting to be published
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Successfully published
    /// </summary>
    Published = 1,

    /// <summary>
    /// Failed after max retries
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Currently being processed
    /// </summary>
    Processing = 3
}

