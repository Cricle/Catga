using Catga.Messages;

namespace Catga.EventSourcing;

/// <summary>Base class for event-sourced aggregates</summary>
public abstract class AggregateRoot
{
    private readonly List<IEvent> _uncommittedEvents = new();

    public string Id { get; protected set; } = string.Empty;
    public long Version { get; protected set; } = -1;
    public IReadOnlyList<IEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    protected void RaiseEvent(IEvent @event)
    {
        Apply(@event);
        _uncommittedEvents.Add(@event);
    }

    protected abstract void Apply(IEvent @event);

    public void LoadFromHistory(IEnumerable<StoredEvent> events)
    {
        foreach (var storedEvent in events)
        {
            Apply(storedEvent.Event);
            Version = storedEvent.Version;
        }
    }

    public void MarkEventsAsCommitted()
    {
        Version += _uncommittedEvents.Count;
        _uncommittedEvents.Clear();
    }
}

