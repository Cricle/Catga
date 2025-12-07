using Catga.Abstractions;

namespace Catga.EventSourcing.Testing;

/// <summary>
/// Test fixture for event store testing.
/// Requires an IEventStore implementation to be provided.
/// </summary>
public sealed class EventStoreFixture : IDisposable
{
    private readonly IEventStore _eventStore;
    private readonly Action? _cleanup;

    public EventStoreFixture(IEventStore eventStore, Action? cleanup = null)
    {
        _eventStore = eventStore;
        _cleanup = cleanup;
    }

    public IEventStore EventStore => _eventStore;

    /// <summary>Seed events into a stream.</summary>
    public async ValueTask SeedAsync(string streamId, IReadOnlyList<IEvent> events)
    {
        await _eventStore.AppendAsync(streamId, events);
    }

    /// <summary>Assert that an event of type T was appended to stream.</summary>
    public async ValueTask AssertEventAppendedAsync<T>(string streamId) where T : IEvent
    {
        var stream = await _eventStore.ReadAsync(streamId);
        var hasEvent = stream.Events.Any(e => e.Event is T);
        if (!hasEvent)
            throw new AssertionException($"Expected event of type {typeof(T).Name} in stream {streamId}");
    }

    /// <summary>Assert event count in stream.</summary>
    public async ValueTask AssertEventCountAsync(string streamId, int expectedCount)
    {
        var stream = await _eventStore.ReadAsync(streamId);
        if (stream.Events.Count != expectedCount)
            throw new AssertionException($"Expected {expectedCount} events in stream {streamId}, but found {stream.Events.Count}");
    }

    /// <summary>Assert no events in stream.</summary>
    public async ValueTask AssertNoEventsAsync(string streamId)
    {
        var stream = await _eventStore.ReadAsync(streamId);
        if (stream.Events.Count > 0)
            throw new AssertionException($"Expected no events in stream {streamId}, but found {stream.Events.Count}");
    }

    public void Dispose()
    {
        _cleanup?.Invoke();
    }
}

/// <summary>
/// Test fixture for aggregate testing.
/// </summary>
public sealed class AggregateFixture<TAggregate> where TAggregate : AggregateRoot, new()
{
    public TAggregate Aggregate { get; } = new();

    /// <summary>Load aggregate with historical events.</summary>
    public AggregateFixture<TAggregate> Given(params IEvent[] events)
    {
        Aggregate.LoadFromHistory(events);
        return this;
    }

    /// <summary>Assert that aggregate has uncommitted event of type T.</summary>
    public void AssertUncommittedEvent<T>() where T : IEvent
    {
        var hasEvent = Aggregate.UncommittedEvents.Any(e => e is T);
        if (!hasEvent)
            throw new AssertionException($"Expected uncommitted event of type {typeof(T).Name}");
    }

    /// <summary>Assert uncommitted event count.</summary>
    public void AssertUncommittedEventCount(int expectedCount)
    {
        var count = Aggregate.UncommittedEvents.Count;
        if (count != expectedCount)
            throw new AssertionException($"Expected {expectedCount} uncommitted events, but found {count}");
    }
}

/// <summary>
/// Replay tester for event sourcing.
/// </summary>
public sealed class ReplayTester<TAggregate> where TAggregate : AggregateRoot, new()
{
    private readonly IEventStore _eventStore;

    public ReplayTester(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    /// <summary>Replay all events to aggregate.</summary>
    public async ValueTask<TAggregate?> ReplayAsync(string streamId, CancellationToken ct = default)
    {
        var stream = await _eventStore.ReadAsync(streamId, cancellationToken: ct);
        if (stream.Events.Count == 0) return null;

        var aggregate = new TAggregate();
        aggregate.LoadFromHistory(stream.Events.Select(e => e.Event));
        return aggregate;
    }

    /// <summary>Replay events up to specific version.</summary>
    public async ValueTask<TAggregate?> ReplayToVersionAsync(string streamId, long version, CancellationToken ct = default)
    {
        var stream = await _eventStore.ReadToVersionAsync(streamId, version, ct);
        if (stream.Events.Count == 0) return null;

        var aggregate = new TAggregate();
        aggregate.LoadFromHistory(stream.Events.Select(e => e.Event));
        return aggregate;
    }
}

/// <summary>
/// BDD-style scenario runner for aggregate testing.
/// </summary>
public sealed class ScenarioRunner<TAggregate> where TAggregate : AggregateRoot, new()
{
    private readonly IEventStore _eventStore;
    private string _streamId = "";
    private readonly List<IEvent> _givenEvents = new();
    private Action<TAggregate>? _whenAction;
    private Action<TAggregate>? _thenAssertion;

    public ScenarioRunner(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    /// <summary>Setup initial events.</summary>
    public ScenarioRunner<TAggregate> Given(string streamId, params IEvent[] events)
    {
        _streamId = streamId;
        _givenEvents.AddRange(events);
        return this;
    }

    /// <summary>Execute action on aggregate.</summary>
    public ScenarioRunner<TAggregate> When(Action<TAggregate> action)
    {
        _whenAction = action;
        return this;
    }

    /// <summary>Assert final state.</summary>
    public ScenarioRunner<TAggregate> Then(Action<TAggregate> assertion)
    {
        _thenAssertion = assertion;
        return this;
    }

    /// <summary>Run the scenario.</summary>
    public async ValueTask RunAsync(CancellationToken ct = default)
    {
        // Seed events
        if (_givenEvents.Count > 0)
        {
            await _eventStore.AppendAsync(_streamId, _givenEvents, cancellationToken: ct);
        }

        // Replay to aggregate
        var aggregate = new TAggregate();
        if (_givenEvents.Count > 0)
        {
            var stream = await _eventStore.ReadAsync(_streamId, cancellationToken: ct);
            aggregate.LoadFromHistory(stream.Events.Select(e => e.Event));
        }

        // Execute action
        _whenAction?.Invoke(aggregate);

        // Assert
        _thenAssertion?.Invoke(aggregate);
    }
}

/// <summary>
/// Assertion exception for test utilities.
/// </summary>
public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
