namespace Catga.Transport;

/// <summary>
/// Message acknowledgment interface for reliable delivery.
/// AOT-compatible design.
/// </summary>
public interface IMessageAcknowledger
{
    /// <summary>Acknowledge successful message processing.</summary>
    ValueTask AckAsync(string messageId, CancellationToken ct = default);

    /// <summary>Negative acknowledge - message will be redelivered.</summary>
    ValueTask NackAsync(string messageId, bool requeue = true, CancellationToken ct = default);

    /// <summary>Reject message - move to dead letter queue.</summary>
    ValueTask RejectAsync(string messageId, string? reason = null, CancellationToken ct = default);
}

/// <summary>
/// Acknowledgment context passed to message handlers.
/// </summary>
public readonly record struct AckContext
{
    /// <summary>Message identifier.</summary>
    public required string MessageId { get; init; }

    /// <summary>Acknowledger instance.</summary>
    public required IMessageAcknowledger Acknowledger { get; init; }

    /// <summary>Delivery attempt number (1-based).</summary>
    public int DeliveryAttempt { get; init; }

    /// <summary>Maximum delivery attempts before DLQ.</summary>
    public int MaxDeliveryAttempts { get; init; }

    /// <summary>Whether this is the last attempt.</summary>
    public bool IsLastAttempt => DeliveryAttempt >= MaxDeliveryAttempts;

    /// <summary>Acknowledge successful processing.</summary>
    public ValueTask AckAsync(CancellationToken ct = default)
        => Acknowledger.AckAsync(MessageId, ct);

    /// <summary>Negative acknowledge for retry.</summary>
    public ValueTask NackAsync(bool requeue = true, CancellationToken ct = default)
        => Acknowledger.NackAsync(MessageId, requeue, ct);

    /// <summary>Reject and move to DLQ.</summary>
    public ValueTask RejectAsync(string? reason = null, CancellationToken ct = default)
        => Acknowledger.RejectAsync(MessageId, reason, ct);
}

/// <summary>
/// Acknowledgment mode for message processing.
/// </summary>
public enum AckMode : byte
{
    /// <summary>Auto-ack after handler completes successfully.</summary>
    Auto = 0,

    /// <summary>Manual ack required from handler.</summary>
    Manual = 1,

    /// <summary>No ack (fire and forget).</summary>
    None = 2
}

/// <summary>
/// Acknowledgment options.
/// </summary>
public sealed class AckOptions
{
    /// <summary>Acknowledgment mode.</summary>
    public AckMode Mode { get; set; } = AckMode.Auto;

    /// <summary>Maximum delivery attempts before DLQ.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Delay before redelivery on nack.</summary>
    public TimeSpan RedeliveryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Enable exponential backoff for redelivery.</summary>
    public bool ExponentialBackoff { get; set; } = true;

    /// <summary>Maximum redelivery delay.</summary>
    public TimeSpan MaxRedeliveryDelay { get; set; } = TimeSpan.FromMinutes(5);
}
