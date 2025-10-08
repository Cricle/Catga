namespace Catga.Messages;

/// <summary>
/// Base marker interface for all messages
/// </summary>
public interface IMessage
{
    /// <summary>
    /// Unique message identifier
    /// </summary>
    public string MessageId { get; }

    /// <summary>
    /// Message creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Correlation ID for tracking related messages
    /// </summary>
    public string? CorrelationId { get; }
}

/// <summary>
/// Base message implementation
/// </summary>
public abstract record MessageBase : IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
}

