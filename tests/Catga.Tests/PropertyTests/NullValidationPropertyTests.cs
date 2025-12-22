using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Catga.Transport;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// Property tests for null input validation across all stores.
/// 
/// Property 14: Null Input Validation
/// Validates: Requirements 22.1, 22.3
/// 
/// Feature: tdd-validation, Property 14: Null Input Validation
/// </summary>
[Trait("Category", "Property")]
[Trait("Backend", "InMemory")]
public class NullValidationPropertyTests
{
    /// <summary>
    /// Property 14.1: EventStore Null StreamId Validation
    /// 
    /// *For any* operation on EventStore with null streamId, the operation SHALL throw
    /// ArgumentNullException or ArgumentException.
    /// 
    /// **Validates: Requirements 22.1**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_NullStreamId_ThrowsArgumentException()
    {
        return Prop.ForAll(
            Arb.From<NonEmptyString>(),
            (eventData) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
                var events = new List<IEvent> { new TestPropertyEvent(eventData.Get, 1) };

                // Act & Assert - All operations with null streamId should throw
                var appendThrows = false;
                var readThrows = false;
                var getVersionThrows = false;

                try
                {
                    store.AppendAsync(null!, events).AsTask().GetAwaiter().GetResult();
                }
                catch (ArgumentNullException)
                {
                    appendThrows = true;
                }
                catch (ArgumentException)
                {
                    appendThrows = true;
                }

                try
                {
                    store.ReadAsync(null!).AsTask().GetAwaiter().GetResult();
                }
                catch (ArgumentNullException)
                {
                    readThrows = true;
                }
                catch (ArgumentException)
                {
                    readThrows = true;
                }

                try
                {
                    store.GetVersionAsync(null!).AsTask().GetAwaiter().GetResult();
                }
                catch (ArgumentNullException)
                {
                    getVersionThrows = true;
                }
                catch (ArgumentException)
                {
                    getVersionThrows = true;
                }

                return appendThrows && readThrows && getVersionThrows;
            });
    }

    /// <summary>
    /// Property 14.2: EventStore Empty StreamId Validation
    /// 
    /// *For any* operation on EventStore with empty streamId, the operation SHALL throw
    /// ArgumentException.
    /// 
    /// **Validates: Requirements 22.1**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property EventStore_EmptyStreamId_ThrowsArgumentException()
    {
        return Prop.ForAll(
            Arb.From<NonEmptyString>(),
            (eventData) =>
            {
                // Arrange
                var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
                var events = new List<IEvent> { new TestPropertyEvent(eventData.Get, 1) };

                // Act & Assert - All operations with empty streamId should throw
                var appendThrows = false;
                var readThrows = false;
                var getVersionThrows = false;

                try
                {
                    store.AppendAsync(string.Empty, events).AsTask().GetAwaiter().GetResult();
                }
                catch (ArgumentException)
                {
                    appendThrows = true;
                }

                try
                {
                    store.ReadAsync(string.Empty).AsTask().GetAwaiter().GetResult();
                }
                catch (ArgumentException)
                {
                    readThrows = true;
                }

                try
                {
                    store.GetVersionAsync(string.Empty).AsTask().GetAwaiter().GetResult();
                }
                catch (ArgumentException)
                {
                    getVersionThrows = true;
                }

                return appendThrows && readThrows && getVersionThrows;
            });
    }

    /// <summary>
    /// Property 14.3: SnapshotStore Null StreamId Validation
    /// 
    /// *For any* operation on SnapshotStore with null streamId, the operation SHALL throw
    /// ArgumentNullException.
    /// 
    /// **Validates: Requirements 22.1**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property SnapshotStore_NullStreamId_ThrowsArgumentNull()
    {
        return Prop.ForAll(
            Arb.From<PositiveInt>(),
            (version) =>
            {
                // Arrange
                var serializer = new TestJsonSerializer();
                var store = new InMemoryEnhancedSnapshotStore(serializer);
                var aggregate = new TestAggregate { Id = "test", Value = version.Get };

                // Act & Assert - Operations with null streamId should throw
                var saveThrows = false;
                var deleteThrows = false;

                try
                {
                    store.SaveAsync(null!, aggregate, version.Get).AsTask().GetAwaiter().GetResult();
                }
                catch (ArgumentNullException)
                {
                    saveThrows = true;
                }

                try
                {
                    store.DeleteAsync(null!).AsTask().GetAwaiter().GetResult();
                }
                catch (ArgumentNullException)
                {
                    deleteThrows = true;
                }

                return saveThrows && deleteThrows;
            });
    }

    /// <summary>
    /// Property 14.4: FlowStore Null FlowId Validation
    /// 
    /// *For any* operation on FlowStore with null flowId, the operation SHALL throw
    /// ArgumentNullException.
    /// 
    /// **Validates: Requirements 22.1**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property FlowStore_NullFlowId_ThrowsArgumentNull()
    {
        return Prop.ForAll(
            Arb.From<NonEmptyString>(),
            (data) =>
            {
                // Arrange
                var store = new InMemoryDslFlowStore();

                // Act & Assert - Operations with null flowId should throw
                var getThrows = false;
                var deleteThrows = false;

                try
                {
                    store.GetAsync<TestFlowState>(null!).GetAwaiter().GetResult();
                }
                catch (ArgumentNullException)
                {
                    getThrows = true;
                }

                try
                {
                    store.DeleteAsync(null!).GetAwaiter().GetResult();
                }
                catch (ArgumentNullException)
                {
                    deleteThrows = true;
                }

                return getThrows && deleteThrows;
            });
    }

    /// <summary>
    /// Property 14.5: Default Guid Handling
    /// 
    /// *For any* store operation with default(Guid) as ID, the operation SHALL either
    /// return null/empty result or handle gracefully without throwing.
    /// 
    /// **Validates: Requirements 22.3**
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property Stores_DefaultGuid_HandlesGracefully()
    {
        return Prop.ForAll(
            Arb.From<PositiveInt>(),
            (seed) =>
            {
                // Arrange
                var defaultGuidString = Guid.Empty.ToString();
                var eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
                var snapshotStore = new InMemoryEnhancedSnapshotStore(new TestJsonSerializer());
                var flowStore = new InMemoryDslFlowStore();

                // Act & Assert - Default Guid should be handled gracefully
                var eventStoreHandles = false;
                var snapshotStoreHandles = false;
                var flowStoreHandles = false;

                try
                {
                    var result = eventStore.ReadAsync(defaultGuidString).AsTask().GetAwaiter().GetResult();
                    eventStoreHandles = result != null && result.Events.Count == 0;
                }
                catch
                {
                    eventStoreHandles = false;
                }

                try
                {
                    var result = snapshotStore.LoadAsync<TestAggregate>(defaultGuidString).AsTask().GetAwaiter().GetResult();
                    snapshotStoreHandles = result == null; // Non-existent returns null
                }
                catch
                {
                    snapshotStoreHandles = false;
                }

                try
                {
                    var result = flowStore.GetAsync<TestFlowState>(defaultGuidString).GetAwaiter().GetResult();
                    flowStoreHandles = result == null; // Non-existent returns null
                }
                catch
                {
                    flowStoreHandles = false;
                }

                return eventStoreHandles && snapshotStoreHandles && flowStoreHandles;
            });
    }

    #region Test Helpers

    private record TestPropertyEvent(string Data, decimal Amount) : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    private class TestAggregate
    {
        public string Id { get; set; } = "";
        public int Value { get; set; }
    }

    private class TestFlowState : IFlowState
    {
        private int _changedMask;
        
        public string? FlowId { get; set; }
        public int CurrentStep { get; set; }
        public bool HasChanges => _changedMask != 0;
        public string Data { get; set; } = "";
        
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames()
        {
            var names = new List<string>();
            if (IsFieldChanged(0)) names.Add(nameof(CurrentStep));
            if (IsFieldChanged(1)) names.Add(nameof(Data));
            return names;
        }
    }

    private class TestJsonSerializer : IMessageSerializer
    {
        public string Name => "TestJson";

        public byte[] Serialize<T>(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        }

        public T Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data)!;

        public T Deserialize<T>(ReadOnlySpan<byte> data) => System.Text.Json.JsonSerializer.Deserialize<T>(data)!;

        public void Serialize<T>(T value, System.Buffers.IBufferWriter<byte> bufferWriter)
        {
            using var writer = new System.Text.Json.Utf8JsonWriter(bufferWriter);
            System.Text.Json.JsonSerializer.Serialize(writer, value);
        }

        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);

        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);

        public object? Deserialize(ReadOnlySpan<byte> data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);

        public void Serialize(object value, Type type, System.Buffers.IBufferWriter<byte> bufferWriter)
        {
            using var writer = new System.Text.Json.Utf8JsonWriter(bufferWriter);
            System.Text.Json.JsonSerializer.Serialize(writer, value, type);
        }
    }

    #endregion
}
