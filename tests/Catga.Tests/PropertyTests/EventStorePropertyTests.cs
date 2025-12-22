using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Catga.Tests.PropertyTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// InMemoryEventStore 属性测试
/// 使用 FsCheck 进行属性测试验证
/// 
/// 注意: FsCheck.Xunit 的 [Property] 特性要求测试类有无参构造函数
/// </summary>
[Trait("Category", "Property")]
[Trait("Backend", "InMemory")]
public class InMemoryEventStorePropertyTests
{

    /// <summary>
    /// Property 1: EventStore Round-Trip Consistency
    /// 
    /// *For any* valid event sequence and stream ID, appending events to the EventStore 
    /// then reading them back SHALL return events with identical EventId, EventType, 
    /// Version, Data, and Timestamp.
    /// 
    /// **Validates: Requirements 1.17**
    /// 
    /// Feature: tdd-validation, Property 1: EventStore Round-Trip Consistency
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_RoundTrip_PreservesAllEventData()
    {
        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            (streamId, events) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

                // Act
                store.AppendAsync(streamId, events).AsTask().GetAwaiter().GetResult();
                var result = store.ReadAsync(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Verify round-trip consistency
                var loadedEvents = result.Events;

                // 1. Same number of events
                if (loadedEvents.Count != events.Count)
                {
                    return false;
                }

                // 2. Each event preserves its data
                for (int i = 0; i < events.Count; i++)
                {
                    var original = events[i];
                    var loaded = loadedEvents[i];

                    // Verify MessageId is preserved
                    if (original.MessageId != loaded.Event.MessageId)
                    {
                        return false;
                    }

                    // Verify EventType is set correctly
                    if (string.IsNullOrEmpty(loaded.EventType))
                    {
                        return false;
                    }

                    // Verify Version is sequential (0-based)
                    if (loaded.Version != i)
                    {
                        return false;
                    }

                    // Verify the event data is the same type
                    if (original.GetType() != loaded.Event.GetType())
                    {
                        return false;
                    }

                    // Verify TestPropertyEvent specific data
                    if (original is TestPropertyEvent originalEvent && loaded.Event is TestPropertyEvent loadedEvent)
                    {
                        if (originalEvent.Data != loadedEvent.Data)
                        {
                            return false;
                        }

                        if (originalEvent.Amount != loadedEvent.Amount)
                        {
                            return false;
                        }
                    }
                }

                return true;
            });
    }

    /// <summary>
    /// Property 2: EventStore Version Invariant
    /// 
    /// *For any* stream with N appended events, the stream version SHALL equal N-1 (0-based indexing).
    /// This means version = eventCount - 1, where the first event has version 0.
    /// 
    /// **Validates: Requirements 1.18**
    /// 
    /// Feature: tdd-validation, Property 2: EventStore Version Invariant
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_Version_EqualsEventCountMinusOne()
    {
        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            (streamId, events) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

                // Act
                store.AppendAsync(streamId, events).AsTask().GetAwaiter().GetResult();
                var version = store.GetVersionAsync(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Version should equal event count minus 1 (0-based indexing)
                // For N events, version = N - 1
                // e.g., 1 event → version 0, 2 events → version 1, etc.
                return version == events.Count - 1;
            });
    }

    /// <summary>
    /// Property 2 (Alternative): EventStore Version Invariant with Multiple Appends
    /// 
    /// *For any* stream with multiple sequential appends totaling N events, 
    /// the stream version SHALL equal N-1 (0-based indexing).
    /// 
    /// **Validates: Requirements 1.18**
    /// 
    /// Feature: tdd-validation, Property 2: EventStore Version Invariant (Multiple Appends)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_Version_AfterMultipleAppends_EqualsTotal()
    {
        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            (streamId, events1, events2) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
                var totalEventCount = events1.Count + events2.Count;

                // Act - Append in two batches
                store.AppendAsync(streamId, events1).AsTask().GetAwaiter().GetResult();
                var versionAfterFirst = store.GetVersionAsync(streamId).AsTask().GetAwaiter().GetResult();
                
                // Verify intermediate version
                if (versionAfterFirst != events1.Count - 1)
                {
                    return false;
                }

                store.AppendAsync(streamId, events2, expectedVersion: versionAfterFirst).AsTask().GetAwaiter().GetResult();
                var finalVersion = store.GetVersionAsync(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Final version should equal total event count minus 1
                return finalVersion == totalEventCount - 1;
            });
    }

    /// <summary>
    /// Property 1 (Alternative): EventStore Round-Trip with Multiple Appends
    /// 
    /// *For any* stream, multiple sequential appends followed by a read SHALL return 
    /// all events in the order they were appended.
    /// 
    /// **Validates: Requirements 1.17**
    /// 
    /// Feature: tdd-validation, Property 1: EventStore Round-Trip Consistency (Multiple Appends)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_MultipleAppends_RoundTrip_PreservesAllData()
    {
        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            (streamId, events1, events2) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
                var allEvents = events1.Concat(events2).ToList();

                // Act - Append in two batches
                store.AppendAsync(streamId, events1).AsTask().GetAwaiter().GetResult();
                var versionAfterFirst = store.GetVersionAsync(streamId).AsTask().GetAwaiter().GetResult();
                
                store.AppendAsync(streamId, events2, expectedVersion: versionAfterFirst).AsTask().GetAwaiter().GetResult();
                var result = store.ReadAsync(streamId).AsTask().GetAwaiter().GetResult();

                // Assert
                var loadedEvents = result.Events;

                // Total count should match
                if (loadedEvents.Count != allEvents.Count)
                {
                    return false;
                }

                // All MessageIds should be preserved in order
                for (int i = 0; i < allEvents.Count; i++)
                {
                    if (allEvents[i].MessageId != loadedEvents[i].Event.MessageId)
                    {
                        return false;
                    }
                }

                return true;
            });
    }

    /// <summary>
    /// Property 3: EventStore Ordering Guarantee
    /// 
    /// *For any* sequence of events appended to a stream, reading the stream SHALL return 
    /// events in the exact order they were appended.
    /// 
    /// **Validates: Requirements 1.2**
    /// 
    /// Feature: tdd-validation, Property 3: EventStore Ordering Guarantee
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_Read_PreservesAppendOrder()
    {
        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            EventGenerators.EventListArbitrary(),
            (streamId, events) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

                // Act
                store.AppendAsync(streamId, events).AsTask().GetAwaiter().GetResult();
                var result = store.ReadAsync(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Verify ordering is preserved
                var loadedEvents = result.Events;

                // 1. Same number of events
                if (loadedEvents.Count != events.Count)
                {
                    return false;
                }

                // 2. Events are returned in the exact order they were appended
                // Verify by comparing MessageIds in sequence
                var originalMessageIds = events.Select(e => e.MessageId).ToList();
                var loadedMessageIds = loadedEvents.Select(e => e.Event.MessageId).ToList();

                return originalMessageIds.SequenceEqual(loadedMessageIds);
            });
    }

    /// <summary>
    /// Property 3 (Alternative): EventStore Ordering Guarantee with Multiple Appends
    /// 
    /// *For any* stream with multiple sequential appends, reading the stream SHALL return 
    /// all events in the exact order they were appended across all batches.
    /// 
    /// **Validates: Requirements 1.2**
    /// 
    /// Feature: tdd-validation, Property 3: EventStore Ordering Guarantee (Multiple Appends)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_MultipleAppends_PreservesAppendOrder()
    {
        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            EventGenerators.SmallEventListArbitrary(),
            (streamId, events1, events2) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
                
                // Generate a third batch using the same generator pattern
                var events3Gen = EventGenerators.SmallEventListArbitrary().Generator;
                var events3 = events3Gen.Sample(1, 1).First();
                
                var allEvents = events1.Concat(events2).Concat(events3).ToList();

                // Act - Append in three batches
                store.AppendAsync(streamId, events1).AsTask().GetAwaiter().GetResult();
                var version1 = store.GetVersionAsync(streamId).AsTask().GetAwaiter().GetResult();
                
                store.AppendAsync(streamId, events2, expectedVersion: version1).AsTask().GetAwaiter().GetResult();
                var version2 = store.GetVersionAsync(streamId).AsTask().GetAwaiter().GetResult();
                
                store.AppendAsync(streamId, events3, expectedVersion: version2).AsTask().GetAwaiter().GetResult();
                var result = store.ReadAsync(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Verify ordering is preserved across all batches
                var loadedEvents = result.Events;

                // 1. Same total number of events
                if (loadedEvents.Count != allEvents.Count)
                {
                    return false;
                }

                // 2. Events are returned in the exact order they were appended
                var originalMessageIds = allEvents.Select(e => e.MessageId).ToList();
                var loadedMessageIds = loadedEvents.Select(e => e.Event.MessageId).ToList();

                return originalMessageIds.SequenceEqual(loadedMessageIds);
            });
    }

    /// <summary>
    /// Property 3 (Version Ordering): EventStore Version Numbers Are Sequential
    /// 
    /// *For any* sequence of events appended to a stream, the version numbers of the 
    /// returned events SHALL be sequential starting from 0.
    /// 
    /// **Validates: Requirements 1.2**
    /// 
    /// Feature: tdd-validation, Property 3: EventStore Ordering Guarantee (Version Sequence)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_Read_VersionsAreSequential()
    {
        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            EventGenerators.EventListArbitrary(),
            (streamId, events) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

                // Act
                store.AppendAsync(streamId, events).AsTask().GetAwaiter().GetResult();
                var result = store.ReadAsync(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Verify version numbers are sequential starting from 0
                var loadedEvents = result.Events;

                for (int i = 0; i < loadedEvents.Count; i++)
                {
                    // Version should equal the index (0-based)
                    if (loadedEvents[i].Version != i)
                    {
                        return false;
                    }
                }

                return true;
            });
    }

    /// <summary>
    /// Property 4: EventStore Concurrent Safety
    /// 
    /// *For any* set of concurrent append operations to the same stream using ExpectedVersion.Any,
    /// no events SHALL be lost and the final version SHALL equal the total number of events 
    /// appended minus one (0-based indexing).
    /// 
    /// **Validates: Requirements 1.19, 24.2**
    /// 
    /// Feature: tdd-validation, Property 4: EventStore Concurrent Safety
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_ConcurrentAppends_NoDataLoss()
    {
        return Prop.ForAll(
            EventGenerators.StreamIdArbitrary(),
            Gen.Choose(2, 10).ToArbitrary(), // Number of concurrent batches (2-10)
            (streamId, batchCount) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
                var allEvents = new System.Collections.Concurrent.ConcurrentBag<IEvent>();
                
                // Generate event batches for each concurrent operation
                var eventBatches = Enumerable.Range(0, batchCount)
                    .Select(_ => EventGenerators.SmallEventListArbitrary().Generator.Sample(1, 1).First())
                    .ToList();

                // Track all events that will be appended
                foreach (var batch in eventBatches)
                {
                    foreach (var evt in batch)
                    {
                        allEvents.Add(evt);
                    }
                }

                // Act - Concurrent appends with ExpectedVersion.Any (-1)
                var tasks = eventBatches.Select(batch =>
                    Task.Run(() => store.AppendAsync(streamId, batch, expectedVersion: -1).AsTask().GetAwaiter().GetResult())
                ).ToArray();

                try
                {
                    Task.WaitAll(tasks);
                }
                catch
                {
                    // If any task fails, the property fails
                    return false;
                }

                // Assert
                var result = store.ReadAsync(streamId).AsTask().GetAwaiter().GetResult();
                var loadedEvents = result.Events;
                var totalExpectedEvents = allEvents.Count;

                // 1. No events should be lost - total count should match
                if (loadedEvents.Count != totalExpectedEvents)
                {
                    return false;
                }

                // 2. All appended event MessageIds should be present in the loaded events
                var loadedMessageIds = loadedEvents.Select(e => e.Event.MessageId).ToHashSet();
                foreach (var evt in allEvents)
                {
                    if (!loadedMessageIds.Contains(evt.MessageId))
                    {
                        return false;
                    }
                }

                // 3. Version should equal total event count minus 1 (0-based)
                var version = store.GetVersionAsync(streamId).AsTask().GetAwaiter().GetResult();
                if (version != totalExpectedEvents - 1)
                {
                    return false;
                }

                // 4. Version numbers should be sequential
                for (int i = 0; i < loadedEvents.Count; i++)
                {
                    if (loadedEvents[i].Version != i)
                    {
                        return false;
                    }
                }

                return true;
            });
    }

    /// <summary>
    /// Property 4 (Alternative): EventStore Concurrent Safety - Different Streams
    /// 
    /// *For any* set of concurrent append operations to different streams,
    /// each stream SHALL contain exactly the events appended to it with no cross-contamination.
    /// 
    /// **Validates: Requirements 1.19, 24.2**
    /// 
    /// Feature: tdd-validation, Property 4: EventStore Concurrent Safety (Different Streams)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_ConcurrentAppends_DifferentStreams_NoContamination()
    {
        return Prop.ForAll(
            Gen.Choose(2, 10).ToArbitrary(), // Number of concurrent streams (2-10)
            (streamCount) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
                var streamEventMap = new System.Collections.Concurrent.ConcurrentDictionary<string, List<IEvent>>();
                
                // Generate unique stream IDs and event batches
                var streamData = Enumerable.Range(0, streamCount)
                    .Select(i =>
                    {
                        var streamId = $"concurrent-stream-{Guid.NewGuid():N}";
                        var events = EventGenerators.SmallEventListArbitrary().Generator.Sample(1, 1).First();
                        streamEventMap[streamId] = events;
                        return (streamId, events);
                    })
                    .ToList();

                // Act - Concurrent appends to different streams
                var tasks = streamData.Select(data =>
                    Task.Run(() => store.AppendAsync(data.streamId, data.events).AsTask().GetAwaiter().GetResult())
                ).ToArray();

                try
                {
                    Task.WaitAll(tasks);
                }
                catch
                {
                    return false;
                }

                // Assert - Each stream should contain exactly its events
                foreach (var (streamId, expectedEvents) in streamEventMap)
                {
                    var result = store.ReadAsync(streamId).AsTask().GetAwaiter().GetResult();
                    var loadedEvents = result.Events;

                    // Count should match
                    if (loadedEvents.Count != expectedEvents.Count)
                    {
                        return false;
                    }

                    // All expected MessageIds should be present
                    var expectedMessageIds = expectedEvents.Select(e => e.MessageId).ToHashSet();
                    var loadedMessageIds = loadedEvents.Select(e => e.Event.MessageId).ToHashSet();

                    if (!expectedMessageIds.SetEquals(loadedMessageIds))
                    {
                        return false;
                    }
                }

                return true;
            });
    }
}
