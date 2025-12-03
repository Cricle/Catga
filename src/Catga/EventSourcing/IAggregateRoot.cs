using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.EventSourcing;

/// <summary>
/// Aggregate root base interface for event sourcing.
/// AOT-compatible design.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>Aggregate identifier.</summary>
    string Id { get; }

    /// <summary>Current version (number of applied events).</summary>
    long Version { get; }

    /// <summary>Uncommitted events pending persistence.</summary>
    IReadOnlyList<IEvent> UncommittedEvents { get; }

    /// <summary>Clear uncommitted events after persistence.</summary>
    void ClearUncommittedEvents();

    /// <summary>Apply event to update state.</summary>
    void Apply(IEvent @event);

    /// <summary>Load from event history.</summary>
    void LoadFromHistory(IEnumerable<IEvent> events);
}

/// <summary>
/// Base class for aggregate roots with event sourcing support.
/// </summary>
public abstract class AggregateRoot : IAggregateRoot
{
    private readonly List<IEvent> _uncommittedEvents = new(4);

    public abstract string Id { get; protected set; }
    public long Version { get; private set; }
    public IReadOnlyList<IEvent> UncommittedEvents => _uncommittedEvents;

    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    /// <summary>Raise and apply a new event.</summary>
    protected void RaiseEvent(IEvent @event)
    {
        ApplyEvent(@event);
        _uncommittedEvents.Add(@event);
    }

    public void Apply(IEvent @event)
    {
        ApplyEvent(@event);
    }

    public void LoadFromHistory(IEnumerable<IEvent> events)
    {
        foreach (var @event in events)
        {
            ApplyEvent(@event);
        }
    }

    private void ApplyEvent(IEvent @event)
    {
        When(@event);
        Version++;
    }

    /// <summary>Apply event to update aggregate state. Override in derived class.</summary>
    protected abstract void When(IEvent @event);
}

/// <summary>
/// Aggregate repository for loading and saving aggregates.
/// </summary>
public interface IAggregateRepository<TAggregate> where TAggregate : class, IAggregateRoot
{
    /// <summary>Load aggregate by ID.</summary>
    ValueTask<TAggregate?> LoadAsync(string id, CancellationToken ct = default);

    /// <summary>Save aggregate (append uncommitted events).</summary>
    ValueTask SaveAsync(TAggregate aggregate, CancellationToken ct = default);
}

/// <summary>
/// Default aggregate repository implementation with snapshot support.
/// </summary>
public sealed class AggregateRepository<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate> : IAggregateRepository<TAggregate>
    where TAggregate : class, IAggregateRoot, new()
{
    private readonly IEventStore _eventStore;
    private readonly ISnapshotStore? _snapshotStore;
    private readonly ISnapshotStrategy? _snapshotStrategy;

    public AggregateRepository(
        IEventStore eventStore,
        ISnapshotStore? snapshotStore = null,
        ISnapshotStrategy? snapshotStrategy = null)
    {
        _eventStore = eventStore;
        _snapshotStore = snapshotStore;
        _snapshotStrategy = snapshotStrategy ?? new EventCountSnapshotStrategy();
    }

    public async ValueTask<TAggregate?> LoadAsync(string id, CancellationToken ct = default)
    {
        var streamId = GetStreamId(id);
        long fromVersion = 0;
        TAggregate? aggregate = null;

        // Try load from snapshot first
        if (_snapshotStore != null)
        {
            var snapshot = await _snapshotStore.LoadAsync<TAggregate>(streamId, ct);
            if (snapshot.HasValue)
            {
                aggregate = snapshot.Value.State;
                fromVersion = snapshot.Value.Version + 1;
            }
        }

        // Load events after snapshot
        var stream = await _eventStore.ReadAsync(streamId, fromVersion, cancellationToken: ct);
        if (stream.Events.Count == 0 && aggregate == null)
            return null;

        aggregate ??= new TAggregate();
        aggregate.LoadFromHistory(stream.Events.Select(e => e.Event));

        return aggregate;
    }

    public async ValueTask SaveAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        var uncommitted = aggregate.UncommittedEvents;
        if (uncommitted.Count == 0)
            return;

        var streamId = GetStreamId(aggregate.Id);
        var expectedVersion = aggregate.Version - uncommitted.Count;

        await _eventStore.AppendAsync(streamId, uncommitted, expectedVersion, ct);
        aggregate.ClearUncommittedEvents();

        // Take snapshot if needed
        if (_snapshotStore != null && _snapshotStrategy != null)
        {
            // Assume last snapshot at version 0 for simplicity
            if (_snapshotStrategy.ShouldTakeSnapshot(aggregate.Version, 0))
            {
                await _snapshotStore.SaveAsync(streamId, aggregate, aggregate.Version, ct);
            }
        }
    }

    private static string GetStreamId(string id) => $"{typeof(TAggregate).Name}-{id}";
}
