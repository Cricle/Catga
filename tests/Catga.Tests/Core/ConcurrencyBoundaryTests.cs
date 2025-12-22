using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Idempotency;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using FluentAssertions;
using NSubstitute;
using System.Collections.Concurrent;
using System.Text;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// 并发边界测试
/// 验证 EventStore、SnapshotStore、IdempotencyStore 在高并发场景下的正确性
/// 
/// **Validates: Requirements 24.1-24.9**
/// </summary>
[Trait("Category", "Boundary")]
[Trait("Category", "Concurrency")]
public class ConcurrencyBoundaryTests
{
    #region EventStore Concurrent Tests (Requirements 24.1-24.3)

    /// <summary>
    /// Tests that 100 concurrent appends to the same stream do not lose any data.
    /// 
    /// **Validates: Requirements 24.1, 24.2**
    /// </summary>
    [Fact]
    public async Task EventStore_100ConcurrentAppends_NoDataLoss()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = "concurrent-test-stream";
        var concurrentAppends = 100;
        var eventsPerAppend = 1;
        var appendedEventIds = new ConcurrentBag<long>();
        var exceptions = new ConcurrentBag<Exception>();

        // Act - 100 concurrent appends to the same stream
        var tasks = Enumerable.Range(0, concurrentAppends)
            .Select(async i =>
            {
                try
                {
                    var evt = new TestConcurrencyEvent($"Event-{i}");
                    appendedEventIds.Add(evt.MessageId);
                    // Use ExpectedVersion.Any (-1) to allow concurrent appends
                    await store.AppendAsync(streamId, [evt], expectedVersion: -1);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All events should be stored (no data loss)
        var result = await store.ReadAsync(streamId);
        var loadedEventIds = result.Events.Select(e => e.Event.MessageId).ToHashSet();

        // All appended events should be present
        foreach (var eventId in appendedEventIds)
        {
            loadedEventIds.Should().Contain(eventId, 
                $"Event with MessageId {eventId} should be present in the store");
        }

        // Total count should match
        result.Events.Should().HaveCount(concurrentAppends * eventsPerAppend);
        
        // No exceptions should have occurred (with ExpectedVersion.Any)
        exceptions.Should().BeEmpty("No exceptions should occur with ExpectedVersion.Any");
    }

    /// <summary>
    /// Tests that concurrent appends to different streams work correctly.
    /// 
    /// **Validates: Requirements 24.3**
    /// </summary>
    [Fact]
    public async Task EventStore_100ConcurrentAppends_DifferentStreams_NoDataLoss()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var concurrentAppends = 100;
        var streamEventMap = new ConcurrentDictionary<string, long>();

        // Act - 100 concurrent appends to different streams
        var tasks = Enumerable.Range(0, concurrentAppends)
            .Select(async i =>
            {
                var streamId = $"stream-{i}";
                var evt = new TestConcurrencyEvent($"Event-{i}");
                streamEventMap[streamId] = evt.MessageId;
                await store.AppendAsync(streamId, [evt]);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - Each stream should have exactly one event
        foreach (var (streamId, expectedEventId) in streamEventMap)
        {
            var result = await store.ReadAsync(streamId);
            result.Events.Should().HaveCount(1);
            result.Events[0].Event.MessageId.Should().Be(expectedEventId);
        }

        // All streams should exist
        var allStreams = await store.GetAllStreamIdsAsync();
        allStreams.Should().HaveCount(concurrentAppends);
    }

    /// <summary>
    /// Tests that concurrent reads and writes to the same stream work correctly.
    /// 
    /// **Validates: Requirements 24.1**
    /// </summary>
    [Fact]
    public async Task EventStore_ConcurrentReadWrite_NoCorruption()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = "read-write-stream";
        var writeCount = 50;
        var readCount = 50;
        var exceptions = new ConcurrentBag<Exception>();

        // Pre-populate with some events
        await store.AppendAsync(streamId, [new TestConcurrencyEvent("Initial")]);

        // Act - Concurrent reads and writes
        var writeTasks = Enumerable.Range(0, writeCount)
            .Select(async i =>
            {
                try
                {
                    await store.AppendAsync(streamId, [new TestConcurrencyEvent($"Write-{i}")], expectedVersion: -1);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

        var readTasks = Enumerable.Range(0, readCount)
            .Select(async i =>
            {
                try
                {
                    var result = await store.ReadAsync(streamId);
                    // Just verify we can read without corruption
                    result.Events.Should().NotBeNull();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

        await Task.WhenAll(writeTasks.Concat(readTasks));

        // Assert - No exceptions and data is consistent
        exceptions.Should().BeEmpty();
        
        var finalResult = await store.ReadAsync(streamId);
        finalResult.Events.Should().HaveCountGreaterOrEqualTo(1);
        
        // Verify version consistency
        var version = await store.GetVersionAsync(streamId);
        version.Should().Be(finalResult.Events.Count - 1);
    }

    #endregion

    #region SnapshotStore Concurrent Tests (Requirements 24.4-24.6)

    /// <summary>
    /// Tests that 100 concurrent saves to the same aggregate result in last-write-wins behavior.
    /// 
    /// **Validates: Requirements 24.4, 24.5**
    /// </summary>
    [Fact]
    public async Task SnapshotStore_100ConcurrentSaves_LastWins()
    {
        // Arrange
        var store = new EnhancedInMemorySnapshotStore();
        var aggregateId = "concurrent-aggregate";
        var concurrentSaves = 100;
        var savedVersions = new ConcurrentBag<long>();

        // Act - 100 concurrent saves with different versions
        var tasks = Enumerable.Range(1, concurrentSaves)
            .Select(async i =>
            {
                var state = new TestConcurrencyAggregate { Id = aggregateId, Value = i };
                await store.SaveAsync(aggregateId, state, i);
                savedVersions.Add(i);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - The latest version should be retrievable
        var loaded = await store.LoadAsync<TestConcurrencyAggregate>(aggregateId);
        loaded.HasValue.Should().BeTrue();
        
        // The loaded version should be one of the saved versions
        savedVersions.Should().Contain(loaded.Value.Version);
        
        // The value should match the version (our test data has Value = Version)
        loaded.Value.State.Value.Should().Be((int)loaded.Value.Version);
    }

    /// <summary>
    /// Tests that concurrent saves to different aggregates work correctly.
    /// 
    /// **Validates: Requirements 24.6**
    /// </summary>
    [Fact]
    public async Task SnapshotStore_100ConcurrentSaves_DifferentAggregates_AllPersisted()
    {
        // Arrange
        var store = new EnhancedInMemorySnapshotStore();
        var concurrentSaves = 100;
        var aggregateValues = new ConcurrentDictionary<string, int>();

        // Act - 100 concurrent saves to different aggregates
        var tasks = Enumerable.Range(0, concurrentSaves)
            .Select(async i =>
            {
                var aggregateId = $"aggregate-{i}";
                var state = new TestConcurrencyAggregate { Id = aggregateId, Value = i * 10 };
                aggregateValues[aggregateId] = i * 10;
                await store.SaveAsync(aggregateId, state, 1);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All aggregates should be persisted correctly
        foreach (var (aggregateId, expectedValue) in aggregateValues)
        {
            var loaded = await store.LoadAsync<TestConcurrencyAggregate>(aggregateId);
            loaded.HasValue.Should().BeTrue($"Aggregate {aggregateId} should exist");
            loaded.Value.State.Value.Should().Be(expectedValue);
        }
    }

    /// <summary>
    /// Tests that concurrent reads and writes to the same aggregate work correctly.
    /// 
    /// **Validates: Requirements 24.4**
    /// </summary>
    [Fact]
    public async Task SnapshotStore_ConcurrentReadWrite_NoCorruption()
    {
        // Arrange
        var store = new EnhancedInMemorySnapshotStore();
        var aggregateId = "read-write-aggregate";
        var writeCount = 50;
        var readCount = 50;
        var exceptions = new ConcurrentBag<Exception>();

        // Pre-populate
        await store.SaveAsync(aggregateId, new TestConcurrencyAggregate { Id = aggregateId, Value = 0 }, 1);

        // Act - Concurrent reads and writes
        var writeTasks = Enumerable.Range(2, writeCount)
            .Select(async i =>
            {
                try
                {
                    await store.SaveAsync(aggregateId, 
                        new TestConcurrencyAggregate { Id = aggregateId, Value = i }, i);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

        var readTasks = Enumerable.Range(0, readCount)
            .Select(async i =>
            {
                try
                {
                    var result = await store.LoadAsync<TestConcurrencyAggregate>(aggregateId);
                    // Just verify we can read without corruption
                    result.HasValue.Should().BeTrue();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

        await Task.WhenAll(writeTasks.Concat(readTasks));

        // Assert - No exceptions
        exceptions.Should().BeEmpty();
        
        var finalResult = await store.LoadAsync<TestConcurrencyAggregate>(aggregateId);
        finalResult.HasValue.Should().BeTrue();
    }

    #endregion

    #region IdempotencyStore Concurrent Tests (Requirements 24.7-24.9)

    /// <summary>
    /// Tests that 100 concurrent marks for the same message ID result in exactly-once semantics.
    /// 
    /// **Validates: Requirements 24.7, 24.8**
    /// </summary>
    [Fact]
    public async Task IdempotencyStore_100ConcurrentMarks_ExactlyOnce()
    {
        // Arrange
        var serializer = CreateMockSerializer();
        var store = new MemoryIdempotencyStore(serializer);
        var messageId = 12345L;
        var concurrentMarks = 100;
        var markResults = new ConcurrentBag<bool>();

        // Act - 100 concurrent marks for the same message ID
        var tasks = Enumerable.Range(0, concurrentMarks)
            .Select(async i =>
            {
                // All marks should succeed (idempotent operation)
                await store.MarkAsProcessedAsync(messageId, $"result-{i}");
                markResults.Add(true);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All marks should complete successfully
        markResults.Should().HaveCount(concurrentMarks);
        markResults.Should().AllBeEquivalentTo(true);

        // The message should be marked as processed
        var isProcessed = await store.HasBeenProcessedAsync(messageId);
        isProcessed.Should().BeTrue();
    }

    /// <summary>
    /// Tests that concurrent marks for different message IDs work correctly.
    /// 
    /// **Validates: Requirements 24.9**
    /// </summary>
    [Fact]
    public async Task IdempotencyStore_100ConcurrentMarks_DifferentMessages_AllProcessed()
    {
        // Arrange
        var serializer = CreateMockSerializer();
        var store = new MemoryIdempotencyStore(serializer);
        var concurrentMarks = 100;
        var messageIds = new ConcurrentBag<long>();

        // Act - 100 concurrent marks for different message IDs
        var tasks = Enumerable.Range(0, concurrentMarks)
            .Select(async i =>
            {
                var messageId = (long)(i + 1000);
                messageIds.Add(messageId);
                await store.MarkAsProcessedAsync(messageId, $"result-{i}");
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - All messages should be marked as processed
        foreach (var messageId in messageIds)
        {
            var isProcessed = await store.HasBeenProcessedAsync(messageId);
            isProcessed.Should().BeTrue($"Message {messageId} should be marked as processed");
        }
    }

    /// <summary>
    /// Tests that concurrent check-and-mark operations work correctly.
    /// 
    /// **Validates: Requirements 24.7**
    /// </summary>
    [Fact]
    public async Task IdempotencyStore_ConcurrentCheckAndMark_NoRaceConditions()
    {
        // Arrange
        var serializer = CreateMockSerializer();
        var store = new MemoryIdempotencyStore(serializer);
        var messageId = 99999L;
        var concurrentOps = 100;
        var exceptions = new ConcurrentBag<Exception>();

        // Act - Concurrent check and mark operations
        var tasks = Enumerable.Range(0, concurrentOps)
            .Select(async i =>
            {
                try
                {
                    // Check first
                    var wasProcessed = await store.HasBeenProcessedAsync(messageId);
                    
                    // Then mark
                    await store.MarkAsProcessedAsync(messageId, $"result-{i}");
                    
                    // Check again
                    var isNowProcessed = await store.HasBeenProcessedAsync(messageId);
                    isNowProcessed.Should().BeTrue();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - No exceptions and message is processed
        exceptions.Should().BeEmpty();
        
        var finalCheck = await store.HasBeenProcessedAsync(messageId);
        finalCheck.Should().BeTrue();
    }

    #endregion

    #region Test Helpers

    private record TestConcurrencyEvent(string Data) : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    private class TestConcurrencyAggregate
    {
        public string Id { get; set; } = "";
        public int Value { get; set; }
    }

    private static IMessageSerializer CreateMockSerializer()
    {
        var serializer = Substitute.For<IMessageSerializer>();

        serializer.Serialize(Arg.Any<object>(), Arg.Any<Type>())
            .Returns(callInfo =>
            {
                var obj = callInfo.ArgAt<object>(0);
                return Encoding.UTF8.GetBytes(obj?.ToString() ?? "null");
            });

        serializer.Deserialize(Arg.Any<byte[]>(), Arg.Any<Type>())
            .Returns(callInfo =>
            {
                var bytes = callInfo.ArgAt<byte[]>(0);
                return Encoding.UTF8.GetString(bytes);
            });

        return serializer;
    }

    #endregion
}
