using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for ProjectionRebuilder.
/// </summary>
public class ProjectionRebuilderTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemoryProjectionCheckpointStore _checkpointStore;

    public ProjectionRebuilderTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _checkpointStore = new InMemoryProjectionCheckpointStore();
    }

    [Fact]
    public async Task RebuildAsync_ResetsProjectionState()
    {
        // Arrange
        var projection = new CountingProjection();
        projection.Count = 100; // Simulate existing state

        var rebuilder = new ProjectionRebuilder<CountingProjection>(
            _eventStore, _checkpointStore, projection, "counting");

        // Act
        await rebuilder.RebuildAsync();

        // Assert - projection should be reset
        projection.WasReset.Should().BeTrue();
    }

    [Fact]
    public async Task RebuildAsync_ReplayAllEvents()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [new TestEvent { Data = "a" }, new TestEvent { Data = "b" }]);
        await _eventStore.AppendAsync("stream-2", [new TestEvent { Data = "c" }]);

        var projection = new CountingProjection();
        var rebuilder = new ProjectionRebuilder<CountingProjection>(
            _eventStore, _checkpointStore, projection, "counting");

        // Act
        await rebuilder.RebuildAsync();

        // Assert
        projection.Count.Should().Be(3);
    }

    [Fact]
    public async Task RebuildAsync_DeletesExistingCheckpoint()
    {
        // Arrange
        await _checkpointStore.SaveAsync(new ProjectionCheckpoint
        {
            ProjectionName = "counting",
            Position = 999
        });

        var projection = new CountingProjection();
        var rebuilder = new ProjectionRebuilder<CountingProjection>(
            _eventStore, _checkpointStore, projection, "counting");

        // Act
        await rebuilder.RebuildAsync();

        // Assert - checkpoint should be recreated with new position
        var checkpoint = await _checkpointStore.LoadAsync("counting");
        checkpoint.Should().NotBeNull();
        checkpoint!.Position.Should().Be(0); // No events
    }

    [Fact]
    public async Task RebuildAsync_SavesNewCheckpointWithCorrectPosition()
    {
        // Arrange
        await _eventStore.AppendAsync("stream-1", [new TestEvent(), new TestEvent(), new TestEvent()]);

        var projection = new CountingProjection();
        var rebuilder = new ProjectionRebuilder<CountingProjection>(
            _eventStore, _checkpointStore, projection, "counting");

        // Act
        await rebuilder.RebuildAsync();

        // Assert
        var checkpoint = await _checkpointStore.LoadAsync("counting");
        checkpoint!.Position.Should().Be(3);
        checkpoint.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RebuildAsync_HandlesEmptyEventStore()
    {
        // Arrange
        var projection = new CountingProjection();
        var rebuilder = new ProjectionRebuilder<CountingProjection>(
            _eventStore, _checkpointStore, projection, "counting");

        // Act
        await rebuilder.RebuildAsync();

        // Assert
        projection.Count.Should().Be(0);
        var checkpoint = await _checkpointStore.LoadAsync("counting");
        checkpoint!.Position.Should().Be(0);
    }

    #region Test helpers

    private record TestEvent : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string Data { get; init; } = "";
    }

    private class CountingProjection : IProjection
    {
        public string Name => "counting";
        public int Count { get; set; }
        public bool WasReset { get; private set; }

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            Count++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            Count = 0;
            WasReset = true;
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
