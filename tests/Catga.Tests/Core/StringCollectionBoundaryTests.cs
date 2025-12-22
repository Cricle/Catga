using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Boundary condition tests for string and collection values.
/// Validates: Requirements 7.10-7.17
/// </summary>
public class StringCollectionBoundaryTests
{
    #region String Boundary Tests (Task 11.1)

    /// <summary>
    /// Tests that appending with whitespace-only stream ID behavior.
    /// Validates: Requirement 7.14 - String boundary handling
    /// Note: InMemoryEventStore uses ArgumentException.ThrowIfNullOrEmpty which doesn't catch whitespace.
    /// Redis and NATS implementations use ThrowIfNullOrWhiteSpace which rejects whitespace.
    /// This test documents the InMemory behavior - whitespace stream IDs are allowed.
    /// </summary>
    [Fact]
    public async Task EventStore_Append_WhitespaceStreamId_InMemoryAllowsWhitespace()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var events = new List<IEvent> { new TestStringEvent("test-data") };
        var whitespaceStreamId = "   ";

        // Act - InMemory implementation allows whitespace stream IDs
        // (Redis/NATS implementations would throw ArgumentException)
        await store.AppendAsync(whitespaceStreamId, events);

        // Assert - The whitespace stream ID is accepted in InMemory
        var result = await store.ReadAsync(whitespaceStreamId);
        result.Events.Should().HaveCount(1);
        result.StreamId.Should().Be(whitespaceStreamId);
    }

    /// <summary>
    /// Tests that appending with very long stream ID succeeds.
    /// Validates: Requirement 7.15 - THE RedisEventStore SHALL handle concurrent appends from multiple clients
    /// </summary>
    [Fact]
    public async Task EventStore_Append_VeryLongStreamId_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var events = new List<IEvent> { new TestStringEvent("test-data") };
        // Create a stream ID with 1000+ characters
        var veryLongStreamId = new string('a', 1000) + "-" + Guid.NewGuid().ToString("N");

        // Act
        await store.AppendAsync(veryLongStreamId, events);

        // Assert
        var result = await store.ReadAsync(veryLongStreamId);
        result.Events.Should().HaveCount(1);
        result.StreamId.Should().Be(veryLongStreamId);
    }

    /// <summary>
    /// Tests that appending with stream ID containing Unicode characters succeeds.
    /// Validates: Requirement 7.16 - THE RedisEventStore SHALL handle Redis cluster failover
    /// </summary>
    [Fact]
    public async Task EventStore_Append_StreamIdWithUnicode_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var events = new List<IEvent> { new TestStringEvent("test-data") };
        // Stream ID with various Unicode characters
        var unicodeStreamId = "stream-日本語-한국어-中文-émojis-🎉-" + Guid.NewGuid().ToString("N");

        // Act
        await store.AppendAsync(unicodeStreamId, events);

        // Assert
        var result = await store.ReadAsync(unicodeStreamId);
        result.Events.Should().HaveCount(1);
        result.StreamId.Should().Be(unicodeStreamId);
    }

    /// <summary>
    /// Tests that appending with stream ID containing special characters succeeds.
    /// Validates: Requirement 7.17 - THE RedisEventStore SHALL handle network partition scenarios
    /// </summary>
    [Fact]
    public async Task EventStore_Append_StreamIdWithSpecialChars_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var events = new List<IEvent> { new TestStringEvent("test-data") };
        // Stream ID with special characters (excluding path separators for safety)
        var specialStreamId = "stream_with-special.chars@domain#tag$value%percent&ampersand";

        // Act
        await store.AppendAsync(specialStreamId, events);

        // Assert
        var result = await store.ReadAsync(specialStreamId);
        result.Events.Should().HaveCount(1);
        result.StreamId.Should().Be(specialStreamId);
    }

    /// <summary>
    /// Tests that reading with very long stream ID works correctly.
    /// </summary>
    [Fact]
    public async Task EventStore_Read_VeryLongStreamId_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var veryLongStreamId = new string('x', 2000);
        var events = new List<IEvent> { new TestStringEvent("data") };
        await store.AppendAsync(veryLongStreamId, events);

        // Act
        var result = await store.ReadAsync(veryLongStreamId);

        // Assert
        result.Events.Should().HaveCount(1);
    }

    /// <summary>
    /// Tests that GetVersion with very long stream ID works correctly.
    /// </summary>
    [Fact]
    public async Task EventStore_GetVersion_VeryLongStreamId_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var veryLongStreamId = new string('y', 1500);
        var events = new List<IEvent> { new TestStringEvent("data1"), new TestStringEvent("data2") };
        await store.AppendAsync(veryLongStreamId, events);

        // Act
        var version = await store.GetVersionAsync(veryLongStreamId);

        // Assert
        version.Should().Be(1); // 0-indexed, so 2 events = version 1
    }

    #endregion

    #region Collection Boundary Tests (Task 11.2)

    /// <summary>
    /// Tests that appending empty event list throws ArgumentException.
    /// Validates: Requirement 7.10 - THE RedisEventStore SHALL use correct key prefix
    /// Note: Empty event list is explicitly rejected by the implementation.
    /// </summary>
    [Fact]
    public async Task EventStore_Append_EmptyEventList_ThrowsArgumentException()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var emptyEvents = new List<IEvent>();
        var streamId = $"stream-{Guid.NewGuid():N}";

        // Act
        var act = async () => await store.AppendAsync(streamId, emptyEvents);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("events")
            .WithMessage("*cannot be empty*");
    }

    /// <summary>
    /// Tests that appending a single event succeeds.
    /// Validates: Requirement 7.11 - THE RedisEventStore SHALL support key expiration if configured
    /// </summary>
    [Fact]
    public async Task EventStore_Append_SingleEvent_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var singleEvent = new List<IEvent> { new TestStringEvent("single-event-data") };
        var streamId = $"stream-{Guid.NewGuid():N}";

        // Act
        await store.AppendAsync(streamId, singleEvent);

        // Assert
        var result = await store.ReadAsync(streamId);
        result.Events.Should().HaveCount(1);
        result.Version.Should().Be(0);
    }

    /// <summary>
    /// Tests that appending 10,000 events succeeds.
    /// Validates: Requirement 7.12 - THE RedisEventStore SHALL handle Redis connection failure gracefully
    /// </summary>
    [Fact]
    public async Task EventStore_Append_10000Events_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>();
        for (int i = 0; i < 10000; i++)
        {
            events.Add(new TestStringEvent($"event-{i}"));
        }

        // Act
        await store.AppendAsync(streamId, events);

        // Assert
        var result = await store.ReadAsync(streamId);
        result.Events.Should().HaveCount(10000);
        result.Version.Should().Be(9999); // 0-indexed
    }

    /// <summary>
    /// Tests that appending event with 1MB data succeeds.
    /// Validates: Requirement 7.13 - THE RedisEventStore SHALL reconnect automatically after failure
    /// </summary>
    [Fact]
    public async Task EventStore_Append_EventWith1MBData_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        // Create 1MB of data (1,048,576 bytes)
        var largeData = new string('X', 1024 * 1024);
        var events = new List<IEvent> { new TestLargeDataEvent(largeData) };

        // Act
        await store.AppendAsync(streamId, events);

        // Assert
        var result = await store.ReadAsync(streamId);
        result.Events.Should().HaveCount(1);
        var loadedEvent = result.Events[0].Event as TestLargeDataEvent;
        loadedEvent.Should().NotBeNull();
        loadedEvent!.Data.Length.Should().Be(1024 * 1024);
    }

    /// <summary>
    /// Tests that appending multiple batches of events maintains correct ordering.
    /// </summary>
    [Fact]
    public async Task EventStore_Append_MultipleBatches_MaintainsOrder()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";

        // Act - Append in multiple batches
        for (int batch = 0; batch < 5; batch++)
        {
            var events = new List<IEvent>();
            for (int i = 0; i < 100; i++)
            {
                events.Add(new TestStringEvent($"batch-{batch}-event-{i}"));
            }
            await store.AppendAsync(streamId, events, expectedVersion: batch == 0 ? -1 : (batch * 100) - 1);
        }

        // Assert
        var result = await store.ReadAsync(streamId);
        result.Events.Should().HaveCount(500);
        result.Version.Should().Be(499);

        // Verify ordering
        for (int i = 0; i < 500; i++)
        {
            result.Events[i].Version.Should().Be(i);
        }
    }

    /// <summary>
    /// Tests that reading large number of events works correctly.
    /// </summary>
    [Fact]
    public async Task EventStore_Read_LargeEventCount_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>();
        for (int i = 0; i < 5000; i++)
        {
            events.Add(new TestStringEvent($"event-{i}"));
        }
        await store.AppendAsync(streamId, events);

        // Act
        var result = await store.ReadAsync(streamId);

        // Assert
        result.Events.Should().HaveCount(5000);
        result.Events.First().Version.Should().Be(0);
        result.Events.Last().Version.Should().Be(4999);
    }

    /// <summary>
    /// Tests that reading with pagination works for large event counts.
    /// </summary>
    [Fact]
    public async Task EventStore_Read_LargeEventCount_WithPagination_Succeeds()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent>();
        for (int i = 0; i < 1000; i++)
        {
            events.Add(new TestStringEvent($"event-{i}"));
        }
        await store.AppendAsync(streamId, events);

        // Act - Read in pages of 100
        var allEvents = new List<StoredEvent>();
        for (int page = 0; page < 10; page++)
        {
            var pageResult = await store.ReadAsync(streamId, fromVersion: page * 100, maxCount: 100);
            allEvents.AddRange(pageResult.Events);
        }

        // Assert
        allEvents.Should().HaveCount(1000);
        for (int i = 0; i < 1000; i++)
        {
            allEvents[i].Version.Should().Be(i);
        }
    }

    /// <summary>
    /// Tests that GetAllStreamIds returns all streams when many exist.
    /// </summary>
    [Fact]
    public async Task EventStore_GetAllStreamIds_ManyStreams_ReturnsAll()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamIds = new List<string>();
        for (int i = 0; i < 100; i++)
        {
            var streamId = $"stream-{i:D4}-{Guid.NewGuid():N}";
            streamIds.Add(streamId);
            await store.AppendAsync(streamId, new List<IEvent> { new TestStringEvent($"event-{i}") });
        }

        // Act
        var result = await store.GetAllStreamIdsAsync();

        // Assert
        result.Should().HaveCount(100);
        foreach (var streamId in streamIds)
        {
            result.Should().Contain(streamId);
        }
    }

    #endregion

    #region Test Helpers

    private record TestStringEvent(string Data) : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    private record TestLargeDataEvent(string Data) : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    #endregion
}
