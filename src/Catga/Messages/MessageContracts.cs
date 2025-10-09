namespace Catga.Messages;

#region Base Message

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

#endregion

#region Request

/// <summary>
/// Request message that expects a response
/// </summary>
public interface IRequest<TResponse> : IMessage
{
}

/// <summary>
/// Request message without response
/// </summary>
public interface IRequest : IMessage
{
}

#endregion

#region Command

/// <summary>
/// Command message - intent to change state
/// </summary>
public interface ICommand<TResult> : IRequest<TResult>
{
}

/// <summary>
/// Command message without result
/// </summary>
public interface ICommand : IRequest
{
}

#endregion

#region Query

/// <summary>
/// Query message - request for data without side effects
/// </summary>
public interface IQuery<TResult> : IRequest<TResult>
{
}

#endregion

#region Event

/// <summary>
/// Event message - something that has happened
/// </summary>
public interface IEvent : IMessage
{
    /// <summary>
    /// Event occurred timestamp
    /// </summary>
    public DateTime OccurredAt { get; }
}

/// <summary>
/// Base event implementation
/// </summary>
public abstract record EventBase : MessageBase, IEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

#endregion

