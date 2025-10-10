namespace Catga.Messages;

/// <summary>
/// Marker interface for all messages (framework use only - users don't need to implement this directly)
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public interface IMessage
{
    /// <summary>
    /// Unique message identifier (auto-generated)
    /// </summary>
    string MessageId => Guid.NewGuid().ToString();

    /// <summary>
    /// Message creation timestamp (auto-generated)
    /// </summary>
    DateTime CreatedAt => DateTime.UtcNow;

    /// <summary>
    /// Correlation ID for tracking related messages
    /// </summary>
    string? CorrelationId => null;
}

/// <summary>
/// Request message that expects a response
/// Simple usage: public record MyRequest(...) : IRequest&lt;MyResponse&gt;;
/// </summary>
public interface IRequest<TResponse> : IMessage
{
}

/// <summary>
/// Request message without response
/// Simple usage: public record MyCommand(...) : IRequest;
/// </summary>
public interface IRequest : IMessage
{
}

/// <summary>
/// Event message - something that has happened
/// Simple usage: public record MyEvent(...) : IEvent;
/// </summary>
public interface IEvent : IMessage
{
    /// <summary>
    /// Event occurred timestamp (auto-generated)
    /// </summary>
    DateTime OccurredAt => DateTime.UtcNow;
}

