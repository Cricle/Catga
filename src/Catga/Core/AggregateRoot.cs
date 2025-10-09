using Catga.Messages;

namespace Catga.EventSourcing;

/// <summary>
/// Base class for event-sourced aggregate roots
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IEvent> _uncommittedEvents = new();

    /// <summary>
    /// Unique identifier for this aggregate
    /// </summary>
    public string Id { get; protected set; } = string.Empty;

    /// <summary>
    /// Current version of the aggregate
    /// </summary>
    public long Version { get; protected set; } = -1;

    /// <summary>
    /// Uncommitted events that haven't been persisted yet
    /// </summary>
    public IReadOnlyList<IEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    /// <summary>
    /// Apply an event and add to uncommitted events
    /// </summary>
    protected void RaiseEvent(IEvent @event)
    {
        Apply(@event);
        _uncommittedEvents.Add(@event);
    }

    /// <summary>
    /// Apply an event to update aggregate state (override in derived classes)
    /// </summary>
    protected abstract void Apply(IEvent @event);

    /// <summary>
    /// Load aggregate from event history
    /// </summary>
    public void LoadFromHistory(IEnumerable<StoredEvent> events)
    {
        foreach (var storedEvent in events)
        {
            Apply(storedEvent.Event);
            Version = storedEvent.Version;
        }
    }

    /// <summary>
    /// Mark all uncommitted events as committed
    /// </summary>
    public void MarkEventsAsCommitted()
    {
        Version += _uncommittedEvents.Count;
        _uncommittedEvents.Clear();
    }
}

