using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Persistence.InMemory.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for InMemory stores.
/// </summary>
public class InMemoryStoreTests
{
    #region InMemoryEventStore Tests

    [Fact]
    public async Task EventStore_AppendAsync_StoresEvents()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

        // Act
        await store.AppendAsync("stream-1", [new TestEvent("a"), new TestEvent("b")]);
        var result = await store.ReadAsync("stream-1");

        // Assert
        result.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task EventStore_ReadAsync_ReturnsEventsInOrder()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        await store.AppendAsync("stream-1", [new TestEvent("first"), new TestEvent("second"), new TestEvent("third")]);

        // Act
        var result = await store.ReadAsync("stream-1");

        // Assert
        result.Events.Select(e => ((TestEvent)e.Event).Data).Should().ContainInOrder("first", "second", "third");
    }

    [Fact]
    public async Task EventStore_ReadAsync_WithFromVersion_SkipsEarlierEvents()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        await store.AppendAsync("stream-1", [new TestEvent("a"), new TestEvent("b"), new TestEvent("c")]);

        // Act
        var result = await store.ReadAsync("stream-1", fromVersion: 1);

        // Assert
        result.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task EventStore_GetAllStreamIdsAsync_ReturnsAllStreams()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        await store.AppendAsync("stream-1", [new TestEvent("a")]);
        await store.AppendAsync("stream-2", [new TestEvent("b")]);
        await store.AppendAsync("stream-3", [new TestEvent("c")]);

        // Act
        var streams = await store.GetAllStreamIdsAsync();

        // Assert
        streams.Should().HaveCount(3);
        streams.Should().Contain(["stream-1", "stream-2", "stream-3"]);
    }

    [Fact]
    public async Task EventStore_GetVersionAsync_ReturnsCorrectVersion()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        await store.AppendAsync("stream-1", [new TestEvent("a"), new TestEvent("b"), new TestEvent("c")]);

        // Act
        var version = await store.GetVersionAsync("stream-1");

        // Assert
        version.Should().Be(2); // 0-indexed, so 3 events = version 2
    }

    #endregion

    #region InMemorySubscriptionStore Tests

    [Fact]
    public async Task SubscriptionStore_SaveAndLoad()
    {
        // Arrange
        var store = new InMemorySubscriptionStore();
        var sub = new PersistentSubscription("test-sub", "orders*");

        // Act
        await store.SaveAsync(sub);
        var loaded = await store.LoadAsync("test-sub");

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("test-sub");
        loaded.StreamPattern.Should().Be("orders*");
    }

    [Fact]
    public async Task SubscriptionStore_ListAsync_ReturnsAll()
    {
        // Arrange
        var store = new InMemorySubscriptionStore();
        await store.SaveAsync(new PersistentSubscription("sub-1", "*"));
        await store.SaveAsync(new PersistentSubscription("sub-2", "orders*"));

        // Act
        var subs = await store.ListAsync();

        // Assert
        subs.Should().HaveCount(2);
    }

    [Fact]
    public async Task SubscriptionStore_DeleteAsync_RemovesSubscription()
    {
        // Arrange
        var store = new InMemorySubscriptionStore();
        await store.SaveAsync(new PersistentSubscription("test-sub", "*"));

        // Act
        await store.DeleteAsync("test-sub");
        var loaded = await store.LoadAsync("test-sub");

        // Assert
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task SubscriptionStore_TryAcquireLock_FirstConsumer_Succeeds()
    {
        // Arrange
        var store = new InMemorySubscriptionStore();
        await store.SaveAsync(new PersistentSubscription("test-sub", "*"));

        // Act
        var result = await store.TryAcquireLockAsync("test-sub", "consumer-1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SubscriptionStore_TryAcquireLock_SecondConsumer_Fails()
    {
        // Arrange
        var store = new InMemorySubscriptionStore();
        await store.SaveAsync(new PersistentSubscription("test-sub", "*"));
        await store.TryAcquireLockAsync("test-sub", "consumer-1");

        // Act
        var result = await store.TryAcquireLockAsync("test-sub", "consumer-2");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubscriptionStore_ReleaseLock_AllowsNewConsumer()
    {
        // Arrange
        var store = new InMemorySubscriptionStore();
        await store.SaveAsync(new PersistentSubscription("test-sub", "*"));
        await store.TryAcquireLockAsync("test-sub", "consumer-1");

        // Act
        await store.ReleaseLockAsync("test-sub", "consumer-1");
        var result = await store.TryAcquireLockAsync("test-sub", "consumer-2");

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region InMemoryProjectionCheckpointStore Tests

    [Fact]
    public async Task CheckpointStore_SaveAndLoad()
    {
        // Arrange
        var store = new InMemoryProjectionCheckpointStore();
        var checkpoint = new ProjectionCheckpoint
        {
            ProjectionName = "test-proj",
            Position = 42,
            LastUpdated = DateTime.UtcNow
        };

        // Act
        await store.SaveAsync(checkpoint);
        var loaded = await store.LoadAsync("test-proj");

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Position.Should().Be(42);
    }

    [Fact]
    public async Task CheckpointStore_DeleteAsync_RemovesCheckpoint()
    {
        // Arrange
        var store = new InMemoryProjectionCheckpointStore();
        await store.SaveAsync(new ProjectionCheckpoint { ProjectionName = "test", Position = 1 });

        // Act
        await store.DeleteAsync("test");
        var loaded = await store.LoadAsync("test");

        // Assert
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task CheckpointStore_Update_OverwritesExisting()
    {
        // Arrange
        var store = new InMemoryProjectionCheckpointStore();
        await store.SaveAsync(new ProjectionCheckpoint { ProjectionName = "test", Position = 10 });

        // Act
        await store.SaveAsync(new ProjectionCheckpoint { ProjectionName = "test", Position = 20 });
        var loaded = await store.LoadAsync("test");

        // Assert
        loaded!.Position.Should().Be(20);
    }

    #endregion

    #region InMemoryEnhancedSnapshotStore Tests

    [Fact]
    public async Task EnhancedSnapshotStore_SaveAndLoad()
    {
        // Arrange
        var store = new EnhancedInMemorySnapshotStore();
        var aggregate = new TestAggregate { Id = "test-1", Value = 42 };

        // Act
        await store.SaveAsync("stream-1", aggregate, 5);
        var loaded = await store.LoadAsync<TestAggregate>("stream-1");

        // Assert
        loaded.HasValue.Should().BeTrue();
        loaded.Value.State.Value.Should().Be(42);
        loaded.Value.Version.Should().Be(5);
    }

    [Fact]
    public async Task EnhancedSnapshotStore_GetSnapshotHistoryAsync()
    {
        // Arrange
        var store = new EnhancedInMemorySnapshotStore();
        await store.SaveAsync("stream-1", new TestAggregate { Id = "1", Value = 1 }, 1);
        await store.SaveAsync("stream-1", new TestAggregate { Id = "1", Value = 2 }, 2);
        await store.SaveAsync("stream-1", new TestAggregate { Id = "1", Value = 3 }, 3);

        // Act
        var history = await store.GetSnapshotHistoryAsync("stream-1");

        // Assert
        history.Should().HaveCount(3);
    }

    [Fact]
    public async Task EnhancedSnapshotStore_LoadAtVersionAsync()
    {
        // Arrange
        var store = new EnhancedInMemorySnapshotStore();
        await store.SaveAsync("stream-1", new TestAggregate { Id = "1", Value = 10 }, 1);
        await store.SaveAsync("stream-1", new TestAggregate { Id = "1", Value = 20 }, 2);

        // Act
        var loaded = await store.LoadAtVersionAsync<TestAggregate>("stream-1", 1);

        // Assert
        loaded.HasValue.Should().BeTrue();
        loaded.Value.State.Value.Should().Be(10);
    }

    [Fact]
    public async Task EnhancedSnapshotStore_DeleteAsync()
    {
        // Arrange
        var store = new EnhancedInMemorySnapshotStore();
        await store.SaveAsync("stream-1", new TestAggregate { Id = "1", Value = 1 }, 1);

        // Act
        await store.DeleteAsync("stream-1");
        var loaded = await store.LoadAsync<TestAggregate>("stream-1");

        // Assert
        loaded.HasValue.Should().BeFalse();
    }

    #endregion

    #region Test helpers

    private record TestEvent(string Data) : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    private class TestAggregate
    {
        public string Id { get; set; } = "";
        public int Value { get; set; }
    }

    #endregion
}
