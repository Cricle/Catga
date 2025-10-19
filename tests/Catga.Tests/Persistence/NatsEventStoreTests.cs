using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Messages;
using Catga.Persistence;
using Moq;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Catga.Tests.Persistence;

/// <summary>
/// NATS JetStream Event Store 测试
/// 测试基于 JetStream 的事件存储、快照（KV Store）
/// </summary>
public class NatsEventStoreTests : IAsyncLifetime
{
    private readonly Mock<INatsConnection> _mockConnection;
    private readonly Mock<INatsJSContext> _mockJetStream;
    private readonly Mock<INatsKVContext> _mockKVStore;
    private readonly Mock<IMessageSerializer> _mockSerializer;
    private NatsEventStore _eventStore = null!;

    public NatsEventStoreTests()
    {
        _mockConnection = new Mock<INatsConnection>();
        _mockJetStream = new Mock<INatsJSContext>();
        _mockKVStore = new Mock<INatsKVContext>();
        _mockSerializer = new Mock<IMessageSerializer>();

        _mockConnection.Setup(x => x.CreateJetStreamContext(It.IsAny<NatsJSOpts>()))
            .Returns(_mockJetStream.Object);
        _mockConnection.Setup(x => x.CreateKeyValueStoreContext(It.IsAny<NatsKVOpts>()))
            .Returns(_mockKVStore.Object);
    }

    public Task InitializeAsync()
    {
        _eventStore = new NatsEventStore(_mockConnection.Object, _mockSerializer.Object);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _eventStore?.Dispose();
        return Task.CompletedTask;
    }

    #region SaveAsync Tests

    [Fact]
    public async Task SaveAsync_SingleEvent_PublishesToJetStream()
    {
        // Arrange
        var aggregateId = "order-123";
        var @event = new OrderCreatedEvent
        {
            OrderId = aggregateId,
            Amount = 100.50m,
            OccurredAt = DateTime.UtcNow
        };
        var serializedData = new byte[] { 1, 2, 3 };
        
        _mockSerializer.Setup(x => x.Serialize(@event)).Returns(serializedData);
        
        var mockPubAck = new Mock<PubAckResponse>();
        mockPubAck.Setup(x => x.Seq).Returns(1);
        
        _mockJetStream.Setup(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsJSPubOpts>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPubAck.Object);

        // Act
        await _eventStore.SaveAsync(aggregateId, new[] { @event });

        // Assert
        _mockJetStream.Verify(x => x.PublishAsync(
            It.Is<string>(s => s.Contains(aggregateId)),
            It.Is<byte[]>(b => b.SequenceEqual(serializedData)),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsJSPubOpts>(),
            It.IsAny<CancellationToken>()), Times.Once);
        
        _mockSerializer.Verify(x => x.Serialize(@event), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_MultipleEvents_PreservesOrder()
    {
        // Arrange
        var aggregateId = "order-123";
        var events = new IEvent[]
        {
            new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m },
            new OrderItemAddedEvent { OrderId = aggregateId, ItemName = "Item1" },
            new OrderItemAddedEvent { OrderId = aggregateId, ItemName = "Item2" }
        };
        
        _mockSerializer.Setup(x => x.Serialize(It.IsAny<IEvent>()))
            .Returns(new byte[] { 1, 2, 3 });
        
        var sequence = 0UL;
        _mockJetStream.Setup(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsJSPubOpts>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var mock = new Mock<PubAckResponse>();
                mock.Setup(m => m.Seq).Returns(++sequence);
                return mock.Object;
            });

        // Act
        await _eventStore.SaveAsync(aggregateId, events);

        // Assert
        _mockJetStream.Verify(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsJSPubOpts>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SaveAsync_WithExpectedVersion_OptimisticConcurrency()
    {
        // Arrange
        var aggregateId = "order-123";
        var @event = new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m };
        var expectedVersion = 5;
        
        _mockSerializer.Setup(x => x.Serialize(@event)).Returns(new byte[] { 1, 2, 3 });
        
        // Mock stream info to get current sequence
        var mockStreamInfo = new Mock<StreamInfo>();
        mockStreamInfo.Setup(x => x.State.LastSeq).Returns((ulong)expectedVersion);
        
        _mockJetStream.Setup(x => x.GetStreamAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockStreamInfo.Object);
        
        var mockPubAck = new Mock<PubAckResponse>();
        mockPubAck.Setup(x => x.Seq).Returns((ulong)(expectedVersion + 1));
        
        _mockJetStream.Setup(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsJSPubOpts>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPubAck.Object);

        // Act
        await _eventStore.SaveAsync(aggregateId, new[] { @event }, expectedVersion);

        // Assert
        _mockJetStream.Verify(x => x.GetStreamAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetEventsAsync Tests

    [Fact]
    public async Task GetEventsAsync_RetrievesFromStream()
    {
        // Arrange
        var aggregateId = "order-123";
        var event1 = new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m };
        var event2 = new OrderItemAddedEvent { OrderId = aggregateId, ItemName = "Item1" };
        
        var serializedEvent1 = new byte[] { 1, 2, 3 };
        var serializedEvent2 = new byte[] { 4, 5, 6 };
        
        var mockMsg1 = CreateMockJSMsg(serializedEvent1, 1);
        var mockMsg2 = CreateMockJSMsg(serializedEvent2, 2);
        
        var mockConsumer = new Mock<INatsJSConsumer>();
        mockConsumer.Setup(x => x.FetchAsync<byte[]>(
            It.IsAny<NatsJSFetchOpts>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(mockMsg1, mockMsg2));
        
        _mockJetStream.Setup(x => x.GetConsumerAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConsumer.Object);
        
        _mockSerializer.SetupSequence(x => x.Deserialize<IEvent>(It.IsAny<byte[]>()))
            .Returns(event1)
            .Returns(event2);

        // Act
        var events = await _eventStore.GetEventsAsync(aggregateId);

        // Assert
        Assert.Equal(2, events.Count);
        Assert.IsType<OrderCreatedEvent>(events[0]);
        Assert.IsType<OrderItemAddedEvent>(events[1]);
    }

    [Fact]
    public async Task GetEventsAsync_FromVersion_RetrievesSubset()
    {
        // Arrange
        var aggregateId = "order-123";
        var fromVersion = 5;
        
        var mockConsumer = new Mock<INatsJSConsumer>();
        mockConsumer.Setup(x => x.FetchAsync<byte[]>(
            It.IsAny<NatsJSFetchOpts>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable<NatsJSMsg<byte[]>>());
        
        _mockJetStream.Setup(x => x.GetConsumerAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConsumer.Object);

        // Act
        await _eventStore.GetEventsAsync(aggregateId, fromVersion);

        // Assert
        mockConsumer.Verify(x => x.FetchAsync<byte[]>(
            It.Is<NatsJSFetchOpts>(opts => opts.MaxMsgs == int.MaxValue),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Snapshot Tests (KV Store)

    [Fact]
    public async Task SaveSnapshotAsync_StoresInKVStore()
    {
        // Arrange
        var aggregateId = "order-123";
        var version = 10;
        var snapshot = new OrderSnapshot
        {
            OrderId = aggregateId,
            TotalAmount = 500m,
            ItemCount = 5
        };
        var serializedData = new byte[] { 1, 2, 3 };
        
        _mockSerializer.Setup(x => x.Serialize(snapshot)).Returns(serializedData);
        
        var mockBucket = new Mock<INatsKVStore>();
        mockBucket.Setup(x => x.PutAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(1UL);
        
        _mockKVStore.Setup(x => x.GetBucketAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockBucket.Object);

        // Act
        await _eventStore.SaveSnapshotAsync(aggregateId, snapshot, version);

        // Assert
        mockBucket.Verify(x => x.PutAsync(
            It.Is<string>(k => k.Contains(aggregateId)),
            It.IsAny<byte[]>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetLatestSnapshotAsync_RetrievesFromKVStore()
    {
        // Arrange
        var aggregateId = "order-123";
        var snapshot = new OrderSnapshot
        {
            OrderId = aggregateId,
            TotalAmount = 500m,
            ItemCount = 5
        };
        var version = 10UL;
        
        var mockEntry = new Mock<NatsKVEntry<byte[]>>();
        mockEntry.Setup(x => x.Value).Returns(new byte[] { 1, 2, 3 });
        mockEntry.Setup(x => x.Revision).Returns(version);
        
        var mockBucket = new Mock<INatsKVStore>();
        mockBucket.Setup(x => x.GetEntryAsync<byte[]>(
            It.IsAny<string>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockEntry.Object);
        
        _mockKVStore.Setup(x => x.GetBucketAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockBucket.Object);
        
        _mockSerializer.Setup(x => x.Deserialize<OrderSnapshot>(It.IsAny<byte[]>()))
            .Returns(snapshot);

        // Act
        var result = await _eventStore.GetLatestSnapshotAsync<OrderSnapshot>(aggregateId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal((int)version, result.Version);
        Assert.Equal(snapshot.OrderId, result.Snapshot.OrderId);
    }

    [Fact]
    public async Task GetLatestSnapshotAsync_NoSnapshot_ReturnsNull()
    {
        // Arrange
        var aggregateId = "order-123";
        
        var mockBucket = new Mock<INatsKVStore>();
        mockBucket.Setup(x => x.GetEntryAsync<byte[]>(
            It.IsAny<string>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((NatsKVEntry<byte[]>)null!);
        
        _mockKVStore.Setup(x => x.GetBucketAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockBucket.Object);

        // Act
        var result = await _eventStore.GetLatestSnapshotAsync<OrderSnapshot>(aggregateId);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Stream Subscription Tests

    [Fact]
    public async Task SubscribeToAggregateAsync_ReceivesNewEvents()
    {
        // Arrange
        var aggregateId = "order-123";
        var receivedEvents = new List<IEvent>();
        var @event = new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m };
        
        var mockMsg = CreateMockJSMsg(new byte[] { 1, 2, 3 }, 1);
        
        var mockConsumer = new Mock<INatsJSConsumer>();
        mockConsumer.Setup(x => x.ConsumeAsync<byte[]>(
            It.IsAny<NatsJSConsumeOpts>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable(mockMsg));
        
        _mockJetStream.Setup(x => x.CreateOrUpdateConsumerAsync(
            It.IsAny<string>(),
            It.IsAny<ConsumerConfig>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConsumer.Object);
        
        _mockSerializer.Setup(x => x.Deserialize<IEvent>(It.IsAny<byte[]>()))
            .Returns(@event);

        // Act
        await _eventStore.SubscribeToAggregateAsync(aggregateId, evt =>
        {
            receivedEvents.Add(evt);
            return Task.CompletedTask;
        });

        // Allow some time for async processing
        await Task.Delay(100);

        // Assert
        Assert.Single(receivedEvents);
        Assert.Equal(@event.OrderId, ((OrderCreatedEvent)receivedEvents[0]).OrderId);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task SaveAsync_PublishFailure_ThrowsException()
    {
        // Arrange
        var aggregateId = "order-123";
        var @event = new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m };
        
        _mockSerializer.Setup(x => x.Serialize(@event)).Returns(new byte[] { 1, 2, 3 });
        _mockJetStream.Setup(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsJSPubOpts>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NatsException("Stream not found"));

        // Act & Assert
        await Assert.ThrowsAsync<NatsException>(() =>
            _eventStore.SaveAsync(aggregateId, new[] { @event }));
    }

    #endregion

    #region Helper Methods

    private static NatsJSMsg<byte[]> CreateMockJSMsg(byte[] data, ulong sequence)
    {
        var mockMsg = new Mock<NatsJSMsg<byte[]>>();
        mockMsg.Setup(x => x.Data).Returns(data);
        mockMsg.Setup(x => x.Metadata).Returns(new NatsJSMsgMetadata
        {
            Sequence = new NatsJSSequencePair { Stream = sequence }
        });
        return mockMsg.Object;
    }

    private static async IAsyncEnumerable<T> CreateAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    #endregion

    #region Test Models

    private record OrderCreatedEvent : IEvent
    {
        public string MessageId { get; init; } = Guid.NewGuid().ToString();
        public required string OrderId { get; init; }
        public required decimal Amount { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    private record OrderItemAddedEvent : IEvent
    {
        public string MessageId { get; init; } = Guid.NewGuid().ToString();
        public required string OrderId { get; init; }
        public required string ItemName { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    private class OrderSnapshot
    {
        public required string OrderId { get; init; }
        public required decimal TotalAmount { get; init; }
        public required int ItemCount { get; init; }
    }

    #endregion
}

