using Catga.Messages;

namespace Catga.EventSourcing;

/// <summary>
/// Guided base class for event-sourced aggregates with immutable state.
/// Users only need to implement 2 methods: GetId and Apply.
/// </summary>
/// <typeparam name="TId">Aggregate identifier type</typeparam>
/// <typeparam name="TState">Aggregate state type (must be a record or class)</typeparam>
public abstract class AggregateRoot<TId, TState>
    where TId : notnull
    where TState : class, new()
{
    private readonly List<IEvent> _uncommittedEvents = [];

    public TId? Id { get; protected set; }
    public long Version { get; private set; } = -1;
    public TState State { get; private set; } = new();
    public IReadOnlyList<IEvent> UncommittedEvents => _uncommittedEvents;

    /// <summary>User implements: Extract aggregate ID from event</summary>
    protected abstract TId GetId(IEvent @event);

    /// <summary>User implements: Apply event to state (pure function, returns new state)</summary>
    protected abstract TState Apply(TState currentState, IEvent @event);

    /// <summary>Raise an event (framework handles everything else)</summary>
    protected void RaiseEvent(IEvent @event)
    {
        // Apply event to get new state
        State = Apply(State, @event);

        // Set ID from first event
        Id ??= GetId(@event);

        // Track uncommitted event
        _uncommittedEvents.Add(@event);
    }

    /// <summary>Load aggregate from event history (framework method)</summary>
    public void LoadFromHistory(IReadOnlyList<StoredEvent> events)
    {
        foreach (var storedEvent in events)
        {
            State = Apply(State, storedEvent.Event);
            Id ??= GetId(storedEvent.Event);
            Version = storedEvent.Version;
        }
    }

    /// <summary>Mark events as committed (framework method)</summary>
    public void MarkEventsAsCommitted()
    {
        Version += _uncommittedEvents.Count;
        _uncommittedEvents.Clear();
    }
}

/// <summary>Legacy base class for backward compatibility</summary>
[Obsolete("Use AggregateRoot<TId, TState> for better type safety and immutable state")]
public abstract class AggregateRoot
{
    private readonly List<IEvent> _uncommittedEvents = [];

    public string Id { get; protected set; } = string.Empty;
    public long Version { get; protected set; } = -1;
    public IReadOnlyList<IEvent> UncommittedEvents => _uncommittedEvents;

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

