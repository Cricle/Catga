using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// TDD tests for enhanced snapshot functionality.
/// Tests are written first, then implementation follows.
/// </summary>
public class SnapshotEnhancedTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly EnhancedInMemorySnapshotStore _snapshotStore;

    public SnapshotEnhancedTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _snapshotStore = new EnhancedInMemorySnapshotStore();
    }

    #region 1. ISnapshotStore Enhanced - Load snapshot at specific version

    [Fact]
    public async Task LoadAtVersionAsync_ReturnsSnapshotAtOrBeforeVersion()
    {
        // Arrange
        var streamId = "TestAggregate-order-1";

        // Save snapshots at different versions
        var state1 = CreateTestAggregate("order-1", 100);
        var state2 = CreateTestAggregate("order-1", 200);
        var state3 = CreateTestAggregate("order-1", 300);

        await _snapshotStore.SaveAsync(streamId, state1, 10);
        await _snapshotStore.SaveAsync(streamId, state2, 20);
        await _snapshotStore.SaveAsync(streamId, state3, 30);

        // Act - Load snapshot at version 25 (should return v20 snapshot)
        var snapshot = await _snapshotStore.LoadAtVersionAsync<TestAggregate>(streamId, 25);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.Value.Version.Should().Be(20);
        snapshot.Value.State.Value.Should().Be(200);
    }

    [Fact]
    public async Task LoadAtVersionAsync_ReturnsNullWhenNoSnapshotBeforeVersion()
    {
        // Arrange
        var streamId = "TestAggregate-order-2";
        var state = CreateTestAggregate("order-2", 100);
        await _snapshotStore.SaveAsync(streamId, state, 50);

        // Act - Load snapshot at version 10 (no snapshot exists before v10)
        var snapshot = await _snapshotStore.LoadAtVersionAsync<TestAggregate>(streamId, 10);

        // Assert
        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task GetSnapshotHistoryAsync_ReturnsAllSnapshots()
    {
        // Arrange
        var streamId = "TestAggregate-order-3";
        await _snapshotStore.SaveAsync(streamId, CreateTestAggregate("order-3", 100), 10);
        await _snapshotStore.SaveAsync(streamId, CreateTestAggregate("order-3", 200), 20);
        await _snapshotStore.SaveAsync(streamId, CreateTestAggregate("order-3", 300), 30);

        // Act
        var history = await _snapshotStore.GetSnapshotHistoryAsync(streamId);

        // Assert
        history.Should().HaveCount(3);
        history[0].Version.Should().Be(10);
        history[1].Version.Should().Be(20);
        history[2].Version.Should().Be(30);
    }

    #endregion

    #region 2. Auto-snapshot strategy

    [Fact]
    public async Task AutoSnapshotManager_TakesSnapshotAtThreshold()
    {
        // Arrange
        var streamId = "TestAggregate-auto-1";
        var strategy = new EventCountSnapshotStrategy(eventThreshold: 5);
        var manager = new AutoSnapshotManager<TestAggregate>(_snapshotStore, strategy);

        // Append 10 events
        var events = Enumerable.Range(1, 10)
            .Select(i => new TestValueChangedEvent { AggregateId = "auto-1", NewValue = i * 10 })
            .Cast<IEvent>()
            .ToArray();
        await _eventStore.AppendAsync(streamId, events);

        // Act - Check and take snapshot if needed
        var aggregate = CreateTestAggregate("auto-1", 100);
        await manager.CheckAndSaveSnapshotAsync(streamId, aggregate, 10);

        // Assert - Snapshot should be taken at version 10 (threshold 5)
        var snapshot = await _snapshotStore.LoadAsync<TestAggregate>(streamId);
        snapshot.Should().NotBeNull();
        snapshot!.Value.Version.Should().Be(10);
    }

    [Fact]
    public async Task AutoSnapshotManager_DoesNotTakeSnapshotBelowThreshold()
    {
        // Arrange
        var streamId = "TestAggregate-auto-2";
        var strategy = new EventCountSnapshotStrategy(eventThreshold: 10);
        var manager = new AutoSnapshotManager<TestAggregate>(_snapshotStore, strategy);

        // Act - Try to save snapshot at version 5 (below threshold)
        var aggregate = CreateTestAggregate("auto-2", 50);
        await manager.CheckAndSaveSnapshotAsync(streamId, aggregate, 5);

        // Assert - No snapshot should be taken
        var snapshot = await _snapshotStore.LoadAsync<TestAggregate>(streamId);
        snapshot.Should().BeNull();
    }

    [Fact]
    public void TimeBasedSnapshotStrategy_ShouldTakeSnapshotAfterInterval()
    {
        // Arrange
        var strategy = new TimeBasedSnapshotStrategy(TimeSpan.FromMinutes(5));
        var lastSnapshotTime = DateTime.UtcNow.AddMinutes(-10);

        // Act
        var shouldTake = strategy.ShouldTakeSnapshot(lastSnapshotTime);

        // Assert
        shouldTake.Should().BeTrue();
    }

    [Fact]
    public void CompositeSnapshotStrategy_CombinesMultipleStrategies()
    {
        // Arrange
        var eventStrategy = new EventCountSnapshotStrategy(eventThreshold: 100);
        var timeStrategy = new TimeBasedSnapshotStrategy(TimeSpan.FromMinutes(5));
        var composite = new CompositeSnapshotStrategy(eventStrategy, timeStrategy);

        // Act - Event threshold not met, but time threshold met
        var shouldTake = composite.ShouldTakeSnapshot(
            currentVersion: 50,
            lastSnapshotVersion: 0,
            lastSnapshotTime: DateTime.UtcNow.AddMinutes(-10));

        // Assert - Should take snapshot because time threshold is met
        shouldTake.Should().BeTrue();
    }

    #endregion

    #region 3. Time travel with snapshots

    [Fact]
    public async Task TimeTravelService_UsesSnapshotForFasterReconstruction()
    {
        // Arrange
        var aggregateId = "order-snap-1";
        var streamId = $"TestAggregate-{aggregateId}";

        // Create 100 events
        var events = new List<IEvent>();
        events.Add(new TestCreatedEvent { AggregateId = aggregateId });
        for (int i = 1; i < 100; i++)
        {
            events.Add(new TestValueChangedEvent { AggregateId = aggregateId, NewValue = i * 10 });
        }
        await _eventStore.AppendAsync(streamId, events.ToArray());

        // Save snapshot at version 50
        var snapshotState = CreateTestAggregate(aggregateId, 500);
        await _snapshotStore.SaveAsync(streamId, snapshotState, 50);

        // Create time travel service with snapshot support
        var timeTravelService = new TimeTravelServiceWithSnapshots<TestAggregate>(_eventStore, _snapshotStore);

        // Act - Get state at version 75 (should use snapshot at 50 + replay 25 events)
        var state = await timeTravelService.GetStateAtVersionAsync(aggregateId, 75);

        // Assert
        state.Should().NotBeNull();
        // Value at v75: events 51-75 set Value to 510, 520, ..., 750
        // The last event at v75 sets Value = 75 * 10 = 750
        state!.Value.Should().Be(750);
    }

    [Fact]
    public async Task TimeTravelService_FallsBackToFullReplayWhenNoSnapshot()
    {
        // Arrange
        var aggregateId = "order-snap-2";
        var streamId = $"TestAggregate-{aggregateId}";

        var events = new List<IEvent>();
        events.Add(new TestCreatedEvent { AggregateId = aggregateId });
        for (int i = 1; i < 20; i++)
        {
            events.Add(new TestValueChangedEvent { AggregateId = aggregateId, NewValue = i * 10 });
        }
        await _eventStore.AppendAsync(streamId, events.ToArray());

        var timeTravelService = new TimeTravelServiceWithSnapshots<TestAggregate>(_eventStore, _snapshotStore);

        // Act - Get state at version 15 (no snapshot, full replay)
        var state = await timeTravelService.GetStateAtVersionAsync(aggregateId, 15);

        // Assert
        state.Should().NotBeNull();
        state!.Value.Should().Be(150); // 15 events * 10 each
    }

    #endregion

    #region 4. Snapshot cleanup/retention

    [Fact]
    public async Task CleanupOldSnapshots_KeepsOnlyRecentSnapshots()
    {
        // Arrange
        var streamId = "TestAggregate-cleanup-1";
        for (int i = 1; i <= 10; i++)
        {
            await _snapshotStore.SaveAsync(streamId, CreateTestAggregate("cleanup-1", i * 100), i * 10);
        }

        // Act - Keep only last 3 snapshots
        await _snapshotStore.CleanupAsync(streamId, keepCount: 3);

        // Assert
        var history = await _snapshotStore.GetSnapshotHistoryAsync(streamId);
        history.Should().HaveCount(3);
        history[0].Version.Should().Be(80);
        history[1].Version.Should().Be(90);
        history[2].Version.Should().Be(100);
    }

    [Fact]
    public async Task DeleteSnapshotsBeforeVersion_RemovesOldSnapshots()
    {
        // Arrange
        var streamId = "TestAggregate-cleanup-2";
        for (int i = 1; i <= 5; i++)
        {
            await _snapshotStore.SaveAsync(streamId, CreateTestAggregate("cleanup-2", i * 100), i * 10);
        }

        // Act - Delete snapshots before version 30
        await _snapshotStore.DeleteBeforeVersionAsync(streamId, 30);

        // Assert
        var history = await _snapshotStore.GetSnapshotHistoryAsync(streamId);
        history.Should().HaveCount(3); // v30, v40, v50
        history.All(s => s.Version >= 30).Should().BeTrue();
    }

    #endregion

    #region Test Domain

    private static TestAggregate CreateTestAggregate(string id, int value)
    {
        var agg = new TestAggregate();
        agg.SetIdAndValue(id, value);
        return agg;
    }

    private class TestAggregate : AggregateRoot
    {
        private string _id = string.Empty;
        public override string Id { get => _id; protected set => _id = value; }
        public int Value { get; set; }

        public void SetIdAndValue(string id, int value) { _id = id; Value = value; }

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case TestCreatedEvent e:
                    _id = e.AggregateId;
                    Value = 0;
                    break;
                case TestValueChangedEvent e:
                    Value = e.NewValue;
                    break;
            }
        }
    }

    private record TestCreatedEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string AggregateId { get; init; }
    }

    private record TestValueChangedEvent : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
        public long? CorrelationId { get; init; }
        public long? CausationId { get; init; }
        public required string AggregateId { get; init; }
        public required int NewValue { get; init; }
    }

    #endregion
}

