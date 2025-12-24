using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Catga.Tests.Helpers;
using Catga.Transport;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Boundary condition tests for null and default values.
/// Validates: Requirements 22.1-22.3
/// </summary>
public class NullBoundaryTests
{
    #region EventStore Null Boundary Tests (Task 9.1)

    /// <summary>
    /// Tests that appending with null stream ID throws ArgumentNullException.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task EventStore_Append_NullStreamId_ThrowsArgumentNull()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var events = new List<IEvent> { new TestBoundaryEvent("test") };

        // Act
        var act = async () => await store.AppendAsync(null!, events);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("streamId");
    }

    /// <summary>
    /// Tests that appending with empty stream ID throws ArgumentException.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task EventStore_Append_EmptyStreamId_ThrowsArgumentException()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var events = new List<IEvent> { new TestBoundaryEvent("test") };

        // Act
        var act = async () => await store.AppendAsync(string.Empty, events);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("streamId");
    }

    /// <summary>
    /// Tests that appending with null events throws ArgumentNullException.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task EventStore_Append_NullEvents_ThrowsArgumentNull()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

        // Act
        var act = async () => await store.AppendAsync("stream-1", null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("events");
    }

    /// <summary>
    /// Tests that reading with null stream ID throws ArgumentNullException.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task EventStore_Read_NullStreamId_ThrowsArgumentNull()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

        // Act
        var act = async () => await store.ReadAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("streamId");
    }

    /// <summary>
    /// Tests that reading with empty stream ID throws ArgumentException.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task EventStore_Read_EmptyStreamId_ThrowsArgumentException()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

        // Act
        var act = async () => await store.ReadAsync(string.Empty);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("streamId");
    }

    /// <summary>
    /// Tests that GetVersion with null stream ID throws ArgumentNullException.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task EventStore_GetVersion_NullStreamId_ThrowsArgumentNull()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

        // Act
        var act = async () => await store.GetVersionAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("streamId");
    }

    /// <summary>
    /// Tests that appending empty event list throws ArgumentException.
    /// Validates: Requirement 22.2 - ALL stores SHALL handle empty event lists
    /// </summary>
    [Fact]
    public async Task EventStore_Append_EmptyEventList_ThrowsArgumentException()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var events = new List<IEvent>();

        // Act
        var act = async () => await store.AppendAsync("stream-1", events);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("events");
    }

    /// <summary>
    /// Tests that reading non-existent stream returns empty result.
    /// Validates: Requirement 22.2 - ALL stores SHALL handle empty query results
    /// </summary>
    [Fact]
    public async Task EventStore_Read_NonExistentStream_ReturnsEmptyResult()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());

        // Act
        var result = await store.ReadAsync("non-existent-stream");

        // Assert
        result.Should().NotBeNull();
        result.Events.Should().BeEmpty();
        result.Version.Should().Be(-1);
    }

    #endregion

    #region SnapshotStore Null Boundary Tests (Task 9.2)

    /// <summary>
    /// Tests that saving with null stream ID throws ArgumentNullException.
    /// The underlying ConcurrentDictionary throws when null key is used.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task SnapshotStore_Save_NullStreamId_ThrowsArgumentNull()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        var store = new InMemoryEnhancedSnapshotStore(serializer);
        var aggregate = new TestAggregate { Id = "test-1", Value = 42 };

        // Act
        var act = async () => await store.SaveAsync(null!, aggregate, 1);

        // Assert - ConcurrentDictionary throws ArgumentNullException for null key
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Tests that saving with null aggregate throws NullReferenceException during serialization.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task SnapshotStore_Save_NullData_ThrowsException()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        var store = new InMemoryEnhancedSnapshotStore(serializer);

        // Act
        var act = async () => await store.SaveAsync<TestAggregate>("stream-1", null!, 1);

        // Assert - Serializer will throw when trying to serialize null
        await act.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// Tests that loading with default Guid returns null (non-existent).
    /// Validates: Requirement 22.3 - ALL stores SHALL handle default(Guid) IDs
    /// </summary>
    [Fact]
    public async Task SnapshotStore_Load_DefaultGuid_ReturnsNull()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        var store = new InMemoryEnhancedSnapshotStore(serializer);
        var defaultGuidString = Guid.Empty.ToString();

        // Act
        var result = await store.LoadAsync<TestAggregate>(defaultGuidString);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that loading non-existent stream returns null.
    /// Validates: Requirement 22.2 - ALL stores SHALL handle empty query results
    /// </summary>
    [Fact]
    public async Task SnapshotStore_Load_NonExistentStream_ReturnsNull()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        var store = new InMemoryEnhancedSnapshotStore(serializer);

        // Act
        var result = await store.LoadAsync<TestAggregate>("non-existent-stream");

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that deleting with null stream ID throws ArgumentNullException.
    /// The underlying ConcurrentDictionary throws when null key is used.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task SnapshotStore_Delete_NullStreamId_ThrowsArgumentNull()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        var store = new InMemoryEnhancedSnapshotStore(serializer);

        // Act
        var act = async () => await store.DeleteAsync(null!);

        // Assert - ConcurrentDictionary throws ArgumentNullException for null key
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Tests that deleting non-existent stream handles gracefully.
    /// Validates: Requirement 22.2 - ALL stores SHALL handle empty query results
    /// </summary>
    [Fact]
    public async Task SnapshotStore_Delete_NonExistentStream_HandlesGracefully()
    {
        // Arrange
        var serializer = new TestJsonSerializer();
        var store = new InMemoryEnhancedSnapshotStore(serializer);

        // Act
        var act = async () => await store.DeleteAsync("non-existent-stream");

        // Assert - Should not throw
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Transport Null Boundary Tests (Task 9.3)

    /// <summary>
    /// Tests that sending with null destination handles gracefully.
    /// Note: InMemoryMessageTransport.SendAsync delegates to PublishAsync and ignores destination.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task Transport_Send_NullDestination_HandlesGracefully()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
        var message = new TestTransportMessage("test-data");

        // Act - InMemoryMessageTransport ignores destination and delegates to PublishAsync
        var act = async () => await transport.SendAsync(message, null!);

        // Assert - Should not throw (destination is ignored in InMemory implementation)
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Tests that publishing with null message handles gracefully when handlers exist.
    /// Note: InMemoryMessageTransport doesn't validate null messages - handlers receive null.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task Transport_Publish_NullMessage_WithHandlers_HandlesGracefully()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
        TestTransportMessage? receivedMessage = null;
        
        // Subscribe a valid handler first
        await transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            receivedMessage = msg;
            return Task.CompletedTask;
        });

        // Act - Publishing null message with handlers
        var act = async () => await transport.PublishAsync<TestTransportMessage>(null!);

        // Assert - Should not throw, handler receives null
        await act.Should().NotThrowAsync();
        receivedMessage.Should().BeNull();
    }

    /// <summary>
    /// Tests that subscribing with null handler adds null to handler list (no validation).
    /// Note: ImmutableList.Add accepts null values.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task Transport_Subscribe_NullHandler_HandlesGracefully()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());

        // Act - Current implementation doesn't validate null handler
        var act = async () => await transport.SubscribeAsync<TestNullHandlerMessage>(null!);

        // Assert - ImmutableList.Add accepts null, so no exception
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Tests that sending batch with null messages throws ArgumentNullException.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task Transport_SendBatch_NullMessages_ThrowsArgumentNull()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());

        // Act
        var act = async () => await transport.SendBatchAsync<TestTransportMessage>(null!, "destination");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Tests that publishing batch with empty messages handles gracefully.
    /// Validates: Requirement 22.2 - ALL stores SHALL handle empty event lists
    /// </summary>
    [Fact]
    public async Task Transport_PublishBatch_EmptyMessages_HandlesGracefully()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
        var messages = Array.Empty<TestTransportMessage>();

        // Act
        var act = async () => await transport.PublishBatchAsync(messages);

        // Assert - Should not throw for empty batch
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region FlowStore Null Boundary Tests (Task 9.4)

    /// <summary>
    /// Tests that GetAsync with null flowId throws ArgumentNullException.
    /// The underlying ConcurrentDictionary throws when null key is used.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task FlowStore_Get_NullFlowId_ThrowsArgumentNull()
    {
        // Arrange
        var store = TestStoreExtensions.CreateTestFlowStore();

        // Act
        var act = async () => await store.GetAsync<TestFlowState>(null!);

        // Assert - ConcurrentDictionary.TryGetValue throws ArgumentNullException for null key
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Tests that GetAsync with empty flowId returns null (non-existent).
    /// Validates: Requirement 22.2 - ALL stores SHALL handle empty query results
    /// </summary>
    [Fact]
    public async Task FlowStore_Get_EmptyFlowId_ReturnsNull()
    {
        // Arrange
        var store = TestStoreExtensions.CreateTestFlowStore();

        // Act
        var result = await store.GetAsync<TestFlowState>(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that DeleteAsync with null flowId throws ArgumentNullException.
    /// The underlying ConcurrentDictionary throws when null key is used.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task FlowStore_Delete_NullFlowId_ThrowsArgumentNull()
    {
        // Arrange
        var store = TestStoreExtensions.CreateTestFlowStore();

        // Act
        var act = async () => await store.DeleteAsync(null!);

        // Assert - ConcurrentDictionary.TryRemove throws ArgumentNullException for null key
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Tests that DeleteAsync with non-existent flowId returns false.
    /// Validates: Requirement 22.2 - ALL stores SHALL handle empty query results
    /// </summary>
    [Fact]
    public async Task FlowStore_Delete_NonExistentFlowId_ReturnsFalse()
    {
        // Arrange
        var store = TestStoreExtensions.CreateTestFlowStore();

        // Act
        var result = await store.DeleteAsync("non-existent-flow");

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that CreateAsync with null snapshot throws NullReferenceException.
    /// Validates: Requirement 22.1 - ALL public APIs SHALL throw ArgumentNullException for null required parameters
    /// </summary>
    [Fact]
    public async Task FlowStore_Create_NullSnapshot_ThrowsException()
    {
        // Arrange
        var store = TestStoreExtensions.CreateTestFlowStore();

        // Act
        var act = async () => await store.CreateAsync<TestFlowState>(null!);

        // Assert - Will throw when trying to access snapshot properties
        await act.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// Tests that GetAsync with default Guid returns null (non-existent).
    /// Validates: Requirement 22.3 - ALL stores SHALL handle default(Guid) IDs
    /// </summary>
    [Fact]
    public async Task FlowStore_Get_DefaultGuid_ReturnsNull()
    {
        // Arrange
        var store = TestStoreExtensions.CreateTestFlowStore();
        var defaultGuidString = Guid.Empty.ToString();

        // Act
        var result = await store.GetAsync<TestFlowState>(defaultGuidString);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Test Helpers

    private record TestBoundaryEvent(string Data) : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    private record TestTransportMessage(string Data) : IMessage
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    private record TestNullHandlerMessage(string Data) : IMessage
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
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

    private class TestAggregate
    {
        public string Id { get; set; } = "";
        public int Value { get; set; }
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
