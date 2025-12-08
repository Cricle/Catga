using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for AggregateRepository.
/// </summary>
public class AggregateRepositoryTests
{
    private readonly InMemoryEventStore _eventStore;

    public AggregateRepositoryTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
    }

    [Fact]
    public async Task LoadAsync_WithNoEvents_ReturnsNull()
    {
        // Arrange
        var repository = new AggregateRepository<TestAggregate>(_eventStore);

        // Act
        var result = await repository.LoadAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_WithEvents_ReturnsHydratedAggregate()
    {
        // Arrange
        var aggregateId = "test-1";
        var streamId = $"TestAggregate-{aggregateId}";
        await _eventStore.AppendAsync(streamId, new IEvent[]
        {
            new TestCreatedEvent { Name = "Test" },
            new TestUpdatedEvent { NewName = "Updated" }
        });

        var repository = new AggregateRepository<TestAggregate>(_eventStore);

        // Act
        var result = await repository.LoadAsync(aggregateId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated");
        result.Version.Should().Be(2);
    }

    [Fact]
    public async Task SaveAsync_WithNoUncommittedEvents_DoesNothing()
    {
        // Arrange
        var repository = new AggregateRepository<TestAggregate>(_eventStore);
        var aggregate = new TestAggregate();

        // Act
        await repository.SaveAsync(aggregate);

        // Assert - No events should be in store
        var streams = await _eventStore.GetAllStreamIdsAsync();
        streams.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_WithUncommittedEvents_AppendsToStore()
    {
        // Arrange
        var repository = new AggregateRepository<TestAggregate>(_eventStore);
        var aggregate = new TestAggregate();
        aggregate.Create("agg-1", "Test Name");

        // Act
        await repository.SaveAsync(aggregate);

        // Assert
        var streamId = "TestAggregate-agg-1";
        var stream = await _eventStore.ReadAsync(streamId, 0);
        stream.Events.Should().HaveCount(1);
        aggregate.UncommittedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_ClearsUncommittedEvents()
    {
        // Arrange
        var repository = new AggregateRepository<TestAggregate>(_eventStore);
        var aggregate = new TestAggregate();
        aggregate.Create("agg-2", "Test");

        // Act
        await repository.SaveAsync(aggregate);

        // Assert
        aggregate.UncommittedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_WithSnapshot_LoadsFromSnapshot()
    {
        // Arrange
        var aggregateId = "snap-1";
        var streamId = $"TestAggregate-{aggregateId}";

        // First create some events in the stream (simulating history before snapshot)
        await _eventStore.AppendAsync(streamId, new IEvent[]
        {
            new TestCreatedEvent { AggregateId = aggregateId, Name = "Initial" },
            new TestUpdatedEvent { NewName = "V2" },
            new TestUpdatedEvent { NewName = "V3" },
            new TestUpdatedEvent { NewName = "V4" },
            new TestUpdatedEvent { NewName = "V5" }
        });

        var snapshotStore = Substitute.For<ISnapshotStore>();
        var snapshotAggregate = new TestAggregate();
        snapshotAggregate.Create(aggregateId, "Snapshot State");
        snapshotStore.LoadAsync<TestAggregate>(streamId, Arg.Any<CancellationToken>())
            .Returns(new Snapshot<TestAggregate>
            {
                StreamId = streamId,
                State = snapshotAggregate,
                Version = 4, // Snapshot at version 4 (after 5 events, 0-indexed)
                Timestamp = DateTime.UtcNow
            });

        // Add event after snapshot (version 5)
        await _eventStore.AppendAsync(streamId, new IEvent[]
        {
            new TestUpdatedEvent { NewName = "After Snapshot" }
        }, 4); // Stream has 5 events, so version is 4 (0-indexed)

        var repository = new AggregateRepository<TestAggregate>(_eventStore, snapshotStore);

        // Act
        var result = await repository.LoadAsync(aggregateId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("After Snapshot");
    }

    [Fact]
    public async Task SaveAsync_WithSnapshotStrategy_TakesSnapshot()
    {
        // Arrange
        var snapshotStore = Substitute.For<ISnapshotStore>();
        var strategy = new EventCountSnapshotStrategy(1); // Snapshot every event

        var repository = new AggregateRepository<TestAggregate>(_eventStore, snapshotStore, strategy);
        var aggregate = new TestAggregate();
        aggregate.Create("snap-2", "Test");

        // Act
        await repository.SaveAsync(aggregate);

        // Assert
        await snapshotStore.Received(1).SaveAsync(
            Arg.Any<string>(),
            Arg.Any<TestAggregate>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_WithoutSnapshotStore_DoesNotTakeSnapshot()
    {
        // Arrange
        var repository = new AggregateRepository<TestAggregate>(_eventStore);
        var aggregate = new TestAggregate();
        aggregate.Create("no-snap", "Test");

        // Act & Assert - Should not throw
        await repository.SaveAsync(aggregate);
    }

    [Fact]
    public async Task LoadAsync_WithSnapshotButNoEvents_ReturnsSnapshotState()
    {
        // Arrange
        var aggregateId = "snap-only";
        var streamId = $"TestAggregate-{aggregateId}";

        var snapshotStore = Substitute.For<ISnapshotStore>();
        var snapshotAggregate = new TestAggregate { Name = "Only Snapshot" };
        snapshotStore.LoadAsync<TestAggregate>(streamId, Arg.Any<CancellationToken>())
            .Returns(new Snapshot<TestAggregate>
            {
                StreamId = streamId,
                State = snapshotAggregate,
                Version = 3,
                Timestamp = DateTime.UtcNow
            });

        var repository = new AggregateRepository<TestAggregate>(_eventStore, snapshotStore);

        // Act
        var result = await repository.LoadAsync(aggregateId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Only Snapshot");
    }

    [Fact]
    public async Task RoundTrip_SaveAndLoad_PreservesState()
    {
        // Arrange
        var repository = new AggregateRepository<TestAggregate>(_eventStore);
        var aggregate = new TestAggregate();
        aggregate.Create("round-trip", "Initial");
        aggregate.UpdateName("Updated");

        // Act
        await repository.SaveAsync(aggregate);
        var loaded = await repository.LoadAsync("round-trip");

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Updated");
        loaded.Version.Should().Be(2);
    }

    #region Test Aggregate

    public class TestAggregate : AggregateRoot
    {
        public override string Id { get; protected set; } = "";
        public string Name { get; set; } = "";

        public void Create(string id, string name)
        {
            RaiseEvent(new TestCreatedEvent { AggregateId = id, Name = name });
        }

        public void UpdateName(string newName)
        {
            RaiseEvent(new TestUpdatedEvent { NewName = newName });
        }

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case TestCreatedEvent e:
                    Id = e.AggregateId;
                    Name = e.Name;
                    break;
                case TestUpdatedEvent e:
                    Name = e.NewName;
                    break;
            }
        }
    }

    public record TestCreatedEvent : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string AggregateId { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public record TestUpdatedEvent : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
        public string NewName { get; init; } = "";
    }

    #endregion
}
