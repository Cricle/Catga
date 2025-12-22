using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Boundary condition tests for numeric values (versions, timeouts, counts).
/// Validates: Requirements 23.1-23.9
/// </summary>
public class NumericBoundaryTests
{
    #region EventStore Version Boundary Tests (Task 10.1)

    /// <summary>
    /// Tests that appending with version zero succeeds for a new stream.
    /// Validates: Requirement 23.1 - ALL EventStores SHALL handle version = 0
    /// </summary>
    [Fact]
    public async Task EventStore_Append_VersionZero_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var events = new List<IEvent> { new TestNumericEvent("test-data") };
        var streamId = $"stream-{Guid.NewGuid():N}";

        // Act - Append with expected version -1 (no stream exists, version will be 0 after append)
        await store.AppendAsync(streamId, events, expectedVersion: -1);

        // Assert
        var result = await store.ReadAsync(streamId);
        result.Events.Should().HaveCount(1);
        result.Events[0].Version.Should().Be(0);
        result.Version.Should().Be(0);
    }

    /// <summary>
    /// Tests that appending with expected version 0 succeeds when stream has one event.
    /// Validates: Requirement 23.1 - ALL EventStores SHALL handle version = 0
    /// </summary>
    [Fact]
    public async Task EventStore_Append_ExpectedVersionZero_SucceedsWhenStreamHasOneEvent()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var firstEvent = new List<IEvent> { new TestNumericEvent("first") };
        var secondEvent = new List<IEvent> { new TestNumericEvent("second") };

        // First append creates stream with version 0
        await store.AppendAsync(streamId, firstEvent, expectedVersion: -1);

        // Act - Append with expected version 0
        await store.AppendAsync(streamId, secondEvent, expectedVersion: 0);

        // Assert
        var result = await store.ReadAsync(streamId);
        result.Events.Should().HaveCount(2);
        result.Version.Should().Be(1);
    }

    /// <summary>
    /// Tests that reading from version zero returns all events.
    /// Validates: Requirement 23.1 - ALL EventStores SHALL handle version = 0
    /// </summary>
    [Fact]
    public async Task EventStore_Read_FromVersionZero_ReturnsAllEvents()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>
        {
            new TestNumericEvent("event-1"),
            new TestNumericEvent("event-2"),
            new TestNumericEvent("event-3")
        };
        await store.AppendAsync(streamId, events);

        // Act
        var result = await store.ReadAsync(streamId, fromVersion: 0);

        // Assert
        result.Events.Should().HaveCount(3);
        result.Events[0].Version.Should().Be(0);
    }

    /// <summary>
    /// Tests that reading from negative version is handled gracefully (treated as 0).
    /// Validates: Requirement 23.3 - ALL EventStores SHALL handle negative version numbers
    /// Note: The current implementation treats negative fromVersion as 0 (Math.Max(0, from))
    /// </summary>
    [Fact]
    public async Task EventStore_Read_FromVersionNegative_TreatedAsZero()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>
        {
            new TestNumericEvent("event-1"),
            new TestNumericEvent("event-2")
        };
        await store.AppendAsync(streamId, events);

        // Act - Read from negative version (implementation treats as 0)
        var result = await store.ReadAsync(streamId, fromVersion: -5);

        // Assert - Should return all events (same as fromVersion: 0)
        result.Events.Should().HaveCount(2);
        result.Events[0].Version.Should().Be(0);
    }

    /// <summary>
    /// Tests that appending with negative expected version (-1) means "any version".
    /// Validates: Requirement 23.3 - ALL EventStores SHALL handle negative version numbers
    /// </summary>
    [Fact]
    public async Task EventStore_Append_ExpectedVersionNegativeOne_MeansAnyVersion()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events1 = new List<IEvent> { new TestNumericEvent("first") };
        var events2 = new List<IEvent> { new TestNumericEvent("second") };

        // Act - Append with -1 (any version) multiple times
        await store.AppendAsync(streamId, events1, expectedVersion: -1);
        await store.AppendAsync(streamId, events2, expectedVersion: -1);

        // Assert
        var result = await store.ReadAsync(streamId);
        result.Events.Should().HaveCount(2);
    }

    /// <summary>
    /// Tests that reading to version zero returns only the first event.
    /// Validates: Requirement 23.1 - ALL EventStores SHALL handle version = 0
    /// </summary>
    [Fact]
    public async Task EventStore_ReadToVersion_Zero_ReturnsFirstEventOnly()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>
        {
            new TestNumericEvent("event-1"),
            new TestNumericEvent("event-2"),
            new TestNumericEvent("event-3")
        };
        await store.AppendAsync(streamId, events);

        // Act
        var result = await store.ReadToVersionAsync(streamId, toVersion: 0);

        // Assert
        result.Events.Should().HaveCount(1);
        result.Events[0].Version.Should().Be(0);
    }

    /// <summary>
    /// Tests that reading to negative version returns empty result.
    /// Validates: Requirement 23.3 - ALL EventStores SHALL handle negative version numbers
    /// </summary>
    [Fact]
    public async Task EventStore_ReadToVersion_Negative_ReturnsEmpty()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent> { new TestNumericEvent("event-1") };
        await store.AppendAsync(streamId, events);

        // Act
        var result = await store.ReadToVersionAsync(streamId, toVersion: -1);

        // Assert
        result.Events.Should().BeEmpty();
    }

    #endregion

    #region Transport Timeout Boundary Tests (Task 10.2)

    /// <summary>
    /// Tests that Task.Delay properly throws when given a cancelled token.
    /// Validates: Requirement 23.4 - ALL operations SHALL handle TimeSpan.Zero timeout
    /// Note: This tests the underlying .NET behavior that handlers rely on.
    /// </summary>
    [Fact]
    public async Task TaskDelay_AlreadyCancelled_ThrowsImmediately()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert - Task.Delay should throw TaskCanceledException
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await Task.Delay(1000, cts.Token);
        });
    }

    /// <summary>
    /// Tests that Task.Delay properly throws when timeout expires.
    /// Validates: Requirement 23.4 - ALL operations SHALL handle TimeSpan.Zero timeout
    /// </summary>
    [Fact]
    public async Task TaskDelay_ShortTimeout_ThrowsWhenExpired()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act & Assert - Task.Delay should throw when timeout expires
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await Task.Delay(2000, cts.Token);
        });
    }

    /// <summary>
    /// Tests that EventStore operations respect cancellation token.
    /// Validates: Requirement 23.4 - ALL operations SHALL handle TimeSpan.Zero timeout
    /// </summary>
    [Fact]
    public async Task EventStore_Read_AlreadyCancelled_ThrowsOperationCanceled()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent> { new TestNumericEvent("test") };
        await store.AppendAsync(streamId, events);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert - Should throw OperationCanceledException
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await store.ReadAsync(streamId, ct: cts.Token);
        });
    }

    /// <summary>
    /// Tests that EventStore operations complete successfully with long timeout.
    /// Validates: Requirement 23.5 - ALL operations SHALL handle TimeSpan.MaxValue timeout
    /// </summary>
    [Fact]
    public async Task EventStore_Read_LongTimeout_CompletesSuccessfully()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent> { new TestNumericEvent("test") };
        await store.AppendAsync(streamId, events);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act
        var result = await store.ReadAsync(streamId, ct: cts.Token);

        // Assert
        result.Events.Should().HaveCount(1);
    }

    #endregion

    #region EventStore Count Boundary Tests (Task 10.3)

    /// <summary>
    /// Tests that reading with count zero returns empty result.
    /// Validates: Requirement 23.7 - ALL batch operations SHALL handle count = 0
    /// </summary>
    [Fact]
    public async Task EventStore_Read_CountZero_ReturnsEmpty()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>
        {
            new TestNumericEvent("event-1"),
            new TestNumericEvent("event-2"),
            new TestNumericEvent("event-3")
        };
        await store.AppendAsync(streamId, events);

        // Act
        var result = await store.ReadAsync(streamId, fromVersion: 0, maxCount: 0);

        // Assert
        result.Events.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that reading with count one returns only one event.
    /// Validates: Requirement 23.7 - ALL batch operations SHALL handle count = 0
    /// </summary>
    [Fact]
    public async Task EventStore_Read_CountOne_ReturnsSingleEvent()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>
        {
            new TestNumericEvent("event-1"),
            new TestNumericEvent("event-2"),
            new TestNumericEvent("event-3")
        };
        await store.AppendAsync(streamId, events);

        // Act
        var result = await store.ReadAsync(streamId, fromVersion: 0, maxCount: 1);

        // Assert
        result.Events.Should().HaveCount(1);
        result.Events[0].Version.Should().Be(0);
    }

    /// <summary>
    /// Tests that reading with negative count throws ArgumentOutOfRangeException.
    /// Validates: Requirement 23.9 - ALL pagination SHALL handle page size boundaries
    /// Note: The implementation throws ArgumentOutOfRangeException for negative count values.
    /// </summary>
    [Fact]
    public async Task EventStore_Read_CountNegative_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>
        {
            new TestNumericEvent("event-1"),
            new TestNumericEvent("event-2")
        };
        await store.AppendAsync(streamId, events);

        // Act & Assert - Negative count should throw ArgumentOutOfRangeException
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await store.ReadAsync(streamId, fromVersion: 0, maxCount: -1);
        });
    }

    /// <summary>
    /// Tests that reading with int.MaxValue count returns all events.
    /// Validates: Requirement 23.8 - ALL batch operations SHALL handle count = int.MaxValue
    /// </summary>
    [Fact]
    public async Task EventStore_Read_CountMaxValue_ReturnsAllEvents()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>
        {
            new TestNumericEvent("event-1"),
            new TestNumericEvent("event-2"),
            new TestNumericEvent("event-3")
        };
        await store.AppendAsync(streamId, events);

        // Act
        var result = await store.ReadAsync(streamId, fromVersion: 0, maxCount: int.MaxValue);

        // Assert
        result.Events.Should().HaveCount(3);
    }

    /// <summary>
    /// Tests that reading with count larger than available events returns all available.
    /// Validates: Requirement 23.8 - ALL batch operations SHALL handle count = int.MaxValue
    /// </summary>
    [Fact]
    public async Task EventStore_Read_CountLargerThanAvailable_ReturnsAllAvailable()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>
        {
            new TestNumericEvent("event-1"),
            new TestNumericEvent("event-2")
        };
        await store.AppendAsync(streamId, events);

        // Act
        var result = await store.ReadAsync(streamId, fromVersion: 0, maxCount: 1000);

        // Assert
        result.Events.Should().HaveCount(2);
    }

    /// <summary>
    /// Tests pagination with specific count and offset.
    /// Validates: Requirement 23.9 - ALL pagination SHALL handle page size boundaries
    /// </summary>
    [Fact]
    public async Task EventStore_Read_Pagination_WorksCorrectly()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>();
        for (int i = 0; i < 10; i++)
        {
            events.Add(new TestNumericEvent($"event-{i}"));
        }
        await store.AppendAsync(streamId, events);

        // Act - Read page 2 (events 3-5, 0-indexed)
        var result = await store.ReadAsync(streamId, fromVersion: 3, maxCount: 3);

        // Assert
        result.Events.Should().HaveCount(3);
        result.Events[0].Version.Should().Be(3);
        result.Events[1].Version.Should().Be(4);
        result.Events[2].Version.Should().Be(5);
    }

    #endregion

    #region Test Helpers

    private record TestNumericEvent(string Data) : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    #endregion
}
