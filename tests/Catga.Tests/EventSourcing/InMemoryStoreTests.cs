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

    #region Version Control Tests (Requirements 1.6-1.10)

    /// <summary>
    /// Tests that appending with expectedVersion = -1 (Any) always succeeds regardless of current stream state.
    /// Validates: Requirement 1.8 - THE InMemoryEventStore SHALL support ExpectedVersion.Any for unconditional append
    /// </summary>
    [Fact]
    public async Task Append_ExpectedVersionAny_AlwaysSucceeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        
        // Act & Assert - First append to new stream with Any (-1)
        await store.AppendAsync("stream-1", [new TestEvent("first")], expectedVersion: -1);
        var version1 = await store.GetVersionAsync("stream-1");
        version1.Should().Be(0);

        // Second append with Any (-1) should also succeed
        await store.AppendAsync("stream-1", [new TestEvent("second")], expectedVersion: -1);
        var version2 = await store.GetVersionAsync("stream-1");
        version2.Should().Be(1);

        // Third append with Any (-1) should also succeed
        await store.AppendAsync("stream-1", [new TestEvent("third")], expectedVersion: -1);
        var version3 = await store.GetVersionAsync("stream-1");
        version3.Should().Be(2);

        // Verify all events are stored
        var result = await store.ReadAsync("stream-1");
        result.Events.Should().HaveCount(3);
    }

    /// <summary>
    /// Tests that appending with expectedVersion = -1 succeeds for a new stream (no existing events).
    /// Validates: Requirement 1.9 - THE InMemoryEventStore SHALL support ExpectedVersion.NoStream for new streams
    /// Note: In this implementation, -1 serves as both "Any" and "NoStream" for new streams.
    /// </summary>
    [Fact]
    public async Task Append_ExpectedVersionNoStream_SucceedsForNewStream()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        
        // Act - Append to a brand new stream with expectedVersion = -1
        await store.AppendAsync("new-stream", [new TestEvent("first-event")], expectedVersion: -1);

        // Assert
        var version = await store.GetVersionAsync("new-stream");
        version.Should().Be(0); // First event has version 0
        
        var result = await store.ReadAsync("new-stream");
        result.Events.Should().HaveCount(1);
        ((TestEvent)result.Events[0].Event).Data.Should().Be("first-event");
    }

    /// <summary>
    /// Tests that appending with the correct expected version succeeds.
    /// Validates: Requirement 1.6 - THE InMemoryEventStore SHALL track stream version correctly after each append
    /// </summary>
    [Fact]
    public async Task Append_CorrectExpectedVersion_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        
        // First append - new stream has version -1, so we use -1 or just append without version check
        await store.AppendAsync("stream-1", [new TestEvent("first")], expectedVersion: -1);
        var currentVersion = await store.GetVersionAsync("stream-1");
        currentVersion.Should().Be(0);

        // Act - Append with correct expected version (0)
        await store.AppendAsync("stream-1", [new TestEvent("second")], expectedVersion: 0);

        // Assert
        var newVersion = await store.GetVersionAsync("stream-1");
        newVersion.Should().Be(1);
        
        var result = await store.ReadAsync("stream-1");
        result.Events.Should().HaveCount(2);
    }

    /// <summary>
    /// Tests that appending with the correct expected version succeeds for multiple sequential appends.
    /// Validates: Requirement 1.6 - THE InMemoryEventStore SHALL track stream version correctly after each append
    /// </summary>
    [Fact]
    public async Task Append_CorrectExpectedVersion_SucceedsForMultipleAppends()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        
        // Act & Assert - Sequential appends with correct expected versions
        await store.AppendAsync("stream-1", [new TestEvent("e1")], expectedVersion: -1);
        (await store.GetVersionAsync("stream-1")).Should().Be(0);

        await store.AppendAsync("stream-1", [new TestEvent("e2")], expectedVersion: 0);
        (await store.GetVersionAsync("stream-1")).Should().Be(1);

        await store.AppendAsync("stream-1", [new TestEvent("e3")], expectedVersion: 1);
        (await store.GetVersionAsync("stream-1")).Should().Be(2);

        await store.AppendAsync("stream-1", [new TestEvent("e4"), new TestEvent("e5")], expectedVersion: 2);
        (await store.GetVersionAsync("stream-1")).Should().Be(4);

        // Verify all events
        var result = await store.ReadAsync("stream-1");
        result.Events.Should().HaveCount(5);
    }

    /// <summary>
    /// Tests that appending with a wrong expected version throws ConcurrencyException.
    /// Validates: Requirement 1.7 - THE InMemoryEventStore SHALL reject append with wrong expected version (optimistic concurrency)
    /// Validates: Requirement 1.10 - THE InMemoryEventStore SHALL throw ConcurrencyException on version conflict
    /// </summary>
    [Fact]
    public async Task Append_WrongExpectedVersion_ThrowsConcurrencyException()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        await store.AppendAsync("stream-1", [new TestEvent("first"), new TestEvent("second")]);
        var currentVersion = await store.GetVersionAsync("stream-1");
        currentVersion.Should().Be(1); // Two events = version 1

        // Act - Try to append with wrong expected version
        var act = async () => await store.AppendAsync("stream-1", [new TestEvent("third")], expectedVersion: 5);

        // Assert
        var exception = await act.Should().ThrowAsync<ConcurrencyException>();
        exception.Which.StreamId.Should().Be("stream-1");
        exception.Which.ExpectedVersion.Should().Be(5);
        exception.Which.ActualVersion.Should().Be(1);
    }

    /// <summary>
    /// Tests that appending with expected version 0 to an existing stream with version > 0 throws ConcurrencyException.
    /// Validates: Requirement 1.7 - THE InMemoryEventStore SHALL reject append with wrong expected version
    /// </summary>
    [Fact]
    public async Task Append_ExpectedVersionZero_OnExistingStream_ThrowsConcurrencyException()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        await store.AppendAsync("stream-1", [new TestEvent("first"), new TestEvent("second"), new TestEvent("third")]);
        var currentVersion = await store.GetVersionAsync("stream-1");
        currentVersion.Should().Be(2); // Three events = version 2

        // Act - Try to append with expected version 0 (stale)
        var act = async () => await store.AppendAsync("stream-1", [new TestEvent("fourth")], expectedVersion: 0);

        // Assert
        var exception = await act.Should().ThrowAsync<ConcurrencyException>();
        exception.Which.ExpectedVersion.Should().Be(0);
        exception.Which.ActualVersion.Should().Be(2);
    }

    /// <summary>
    /// Tests that appending with a lower expected version than actual throws ConcurrencyException.
    /// Validates: Requirement 1.7 - THE InMemoryEventStore SHALL reject append with wrong expected version
    /// </summary>
    [Fact]
    public async Task Append_LowerExpectedVersion_ThrowsConcurrencyException()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        // Append 5 events to get version 4
        await store.AppendAsync("stream-1", [
            new TestEvent("e1"), new TestEvent("e2"), new TestEvent("e3"), 
            new TestEvent("e4"), new TestEvent("e5")
        ]);
        var currentVersion = await store.GetVersionAsync("stream-1");
        currentVersion.Should().Be(4);

        // Act - Try to append with expected version 2 (lower than actual 4)
        var act = async () => await store.AppendAsync("stream-1", [new TestEvent("e6")], expectedVersion: 2);

        // Assert
        var exception = await act.Should().ThrowAsync<ConcurrencyException>();
        exception.Which.ExpectedVersion.Should().Be(2);
        exception.Which.ActualVersion.Should().Be(4);
    }

    /// <summary>
    /// Tests that appending with a higher expected version than actual throws ConcurrencyException.
    /// Validates: Requirement 1.7 - THE InMemoryEventStore SHALL reject append with wrong expected version
    /// </summary>
    [Fact]
    public async Task Append_HigherExpectedVersion_ThrowsConcurrencyException()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        await store.AppendAsync("stream-1", [new TestEvent("e1")]);
        var currentVersion = await store.GetVersionAsync("stream-1");
        currentVersion.Should().Be(0);

        // Act - Try to append with expected version 10 (higher than actual 0)
        var act = async () => await store.AppendAsync("stream-1", [new TestEvent("e2")], expectedVersion: 10);

        // Assert
        var exception = await act.Should().ThrowAsync<ConcurrencyException>();
        exception.Which.ExpectedVersion.Should().Be(10);
        exception.Which.ActualVersion.Should().Be(0);
    }

    #endregion

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
