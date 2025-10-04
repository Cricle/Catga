namespace Catga.Messages;

/// <summary>
/// Event message - something that has happened
/// </summary>
public interface IEvent : IMessage
{
    /// <summary>
    /// Event occurred timestamp
    /// </summary>
    DateTime OccurredAt { get; }
}

/// <summary>
/// Base event implementation
/// </summary>
public abstract record EventBase : MessageBase, IEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

