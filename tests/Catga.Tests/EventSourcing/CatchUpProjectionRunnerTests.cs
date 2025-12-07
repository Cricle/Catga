using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for CatchUpProjectionRunner.
/// </summary>
public class CatchUpProjectionRunnerTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemoryProjectionCheckpointStore _checkpointStore;

    public CatchUpProjectionRunnerTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _checkpointStore = new InMemoryProjectionCheckpointStore();
    }

    [Fact]
    public async Task RunAsync_ProcessesAllEventsFromStart()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [new TestEvent("a"), new TestEvent("b")]);
        await _eventStore.AppendAsync("stream-2", [new TestEvent("c")]);

        var projection = new AccumulatingProjection();
        var runner = new CatchUpProjectionRunner<AccumulatingProjection>(
            _eventStore, _checkpointStore, projection, "accumulating");

        // Act
        await runner.RunAsync();

        // Assert
        projection.Events.Should().HaveCount(3);
        projection.Events.Should().Contain(["a", "b", "c"]);
    }

    [Fact]
    public async Task RunAsync_ResumesFromCheckpoint()
    {
        // Arrange - first run to process some events
        await _eventStore.AppendAsync("stream-1", [new TestEvent("a"), new TestEvent("b")]);

        var projection = new AccumulatingProjection();
        var runner = new CatchUpProjectionRunner<AccumulatingProjection>(
            _eventStore, _checkpointStore, projection, "accumulating");

        // First run - processes a and b
        await runner.RunAsync();
        projection.Events.Should().HaveCount(2);

        // Add more events
        await _eventStore.AppendAsync("stream-1", [new TestEvent("c")]);

        // Act - second run should only process new events
        await runner.RunAsync();

        // Assert - should have all 3 events, but c was added in second run
        projection.Events.Should().HaveCount(3);
        projection.Events.Should().Contain(["a", "b", "c"]);
    }

    [Fact]
    public async Task RunAsync_UpdatesCheckpointAfterProcessing()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [new TestEvent("a"), new TestEvent("b")]);

        var projection = new AccumulatingProjection();
        var runner = new CatchUpProjectionRunner<AccumulatingProjection>(
            _eventStore, _checkpointStore, projection, "accumulating");

        // Act
        await runner.RunAsync();

        // Assert
        var checkpoint = await _checkpointStore.LoadAsync("accumulating");
        checkpoint.Should().NotBeNull();
        checkpoint!.Position.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RunAsync_HandlesEmptyEventStore()
    {
        // Arrange
        var projection = new AccumulatingProjection();
        var runner = new CatchUpProjectionRunner<AccumulatingProjection>(
            _eventStore, _checkpointStore, projection, "accumulating");

        // Act
        await runner.RunAsync();

        // Assert
        projection.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_MultipleRuns_ProcessesNewEventsOnly()
    {
        // Arrange
        var projection = new AccumulatingProjection();
        var runner = new CatchUpProjectionRunner<AccumulatingProjection>(
            _eventStore, _checkpointStore, projection, "accumulating");

        await _eventStore.AppendAsync("stream-1", [new TestEvent("a")]);
        await runner.RunAsync();

        await _eventStore.AppendAsync("stream-1", [new TestEvent("b")]);

        // Act - run again
        await runner.RunAsync();

        // Assert - should have both events, but 'a' only once
        projection.Events.Count(e => e == "a").Should().Be(1);
        projection.Events.Should().Contain("b");
    }

    [Fact]
    public async Task RunAsync_MultipleStreams_ProcessesAll()
    {
        // Arrange
        await _eventStore.AppendAsync("orders-1", [new TestEvent("order1")]);
        await _eventStore.AppendAsync("orders-2", [new TestEvent("order2")]);
        await _eventStore.AppendAsync("customers-1", [new TestEvent("customer1")]);

        var projection = new AccumulatingProjection();
        var runner = new CatchUpProjectionRunner<AccumulatingProjection>(
            _eventStore, _checkpointStore, projection, "accumulating");

        // Act
        await runner.RunAsync();

        // Assert
        projection.Events.Should().HaveCount(3);
    }

    #region Test helpers

    private record TestEvent(string Data) : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    private class AccumulatingProjection : IProjection
    {
        public string Name => "accumulating";
        public List<string> Events { get; } = [];

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            if (@event is TestEvent te)
                Events.Add(te.Data);
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            Events.Clear();
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
