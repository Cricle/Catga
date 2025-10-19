using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Messages;
using Catga.Persistence;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Catga.Tests.Persistence;

/// <summary>
/// Redis Event Store 测试
/// 测试事件存储、快照管理、并发控制
/// </summary>
/// <remarks>
/// TODO: RedisEventStore implementation is pending.
/// These tests serve as specification for future implementation.
/// </remarks>
public class RedisEventStoreTests : IAsyncLifetime
{
    private readonly Mock<IConnectionMultiplexer> _mockConnection;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<IMessageSerializer> _mockSerializer;
    private RedisEventStore _eventStore = null!;

    public RedisEventStoreTests()
    {
        _mockConnection = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockSerializer = new Mock<IMessageSerializer>();

        _mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);
    }

    public Task InitializeAsync()
    {
        _eventStore = new RedisEventStore(_mockConnection.Object, _mockSerializer.Object);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _eventStore?.Dispose();
        return Task.CompletedTask;
    }

    #region SaveAsync Tests

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task SaveAsync_SingleEvent_Success()
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
        _mockDatabase.Setup(x => x.ListRightPushAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        // Act
        await _eventStore.SaveAsync(aggregateId, new[] { @event });

        // Assert
        _mockDatabase.Verify(x => x.ListRightPushAsync(
            It.Is<RedisKey>(k => k.ToString().Contains(aggregateId)),
            It.Is<RedisValue>(v => v.ToString() == Convert.ToBase64String(serializedData)),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
        
        _mockSerializer.Verify(x => x.Serialize(@event), Times.Once);
    }

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task SaveAsync_MultipleEvents_PreservesOrder()
    {
        // Arrange
        var aggregateId = "order-123";
        var events = new IEvent[]
        {
            new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m, OccurredAt = DateTime.UtcNow },
            new OrderItemAddedEvent { OrderId = aggregateId, ItemName = "Item1", OccurredAt = DateTime.UtcNow },
            new OrderItemAddedEvent { OrderId = aggregateId, ItemName = "Item2", OccurredAt = DateTime.UtcNow }
        };
        
        _mockSerializer.Setup(x => x.Serialize(It.IsAny<IEvent>()))
            .Returns(new byte[] { 1, 2, 3 });
        
        _mockDatabase.Setup(x => x.ListRightPushAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(3);

        // Act
        await _eventStore.SaveAsync(aggregateId, events);

        // Assert
        _mockDatabase.Verify(x => x.ListRightPushAsync(
            It.Is<RedisKey>(k => k.ToString().Contains(aggregateId)),
            It.Is<RedisValue[]>(arr => arr.Length == 3),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task SaveAsync_WithExpectedVersion_OptimisticConcurrency()
    {
        // Arrange
        var aggregateId = "order-123";
        var @event = new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m };
        var expectedVersion = 5;
        
        _mockSerializer.Setup(x => x.Serialize(@event)).Returns(new byte[] { 1, 2, 3 });
        _mockDatabase.Setup(x => x.ListLengthAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(expectedVersion);
        
        _mockDatabase.Setup(x => x.ListRightPushAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(expectedVersion + 1);

        // Act
        await _eventStore.SaveAsync(aggregateId, new[] { @event }, expectedVersion);

        // Assert
        _mockDatabase.Verify(x => x.ListLengthAsync(
            It.Is<RedisKey>(k => k.ToString().Contains(aggregateId)),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task SaveAsync_ConcurrencyConflict_ThrowsException()
    {
        // Arrange
        var aggregateId = "order-123";
        var @event = new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m };
        var expectedVersion = 5;
        var actualVersion = 7; // Conflict!
        
        _mockSerializer.Setup(x => x.Serialize(@event)).Returns(new byte[] { 1, 2, 3 });
        _mockDatabase.Setup(x => x.ListLengthAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(actualVersion);

        // Act & Assert
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _eventStore.SaveAsync(aggregateId, new[] { @event }, expectedVersion));
    }

    #endregion

    #region GetEventsAsync Tests

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task GetEventsAsync_RetrievesInOrder()
    {
        // Arrange
        var aggregateId = "order-123";
        var event1 = new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m };
        var event2 = new OrderItemAddedEvent { OrderId = aggregateId, ItemName = "Item1" };
        
        var serializedEvents = new[]
        {
            new RedisValue(Convert.ToBase64String(new byte[] { 1, 2, 3 })),
            new RedisValue(Convert.ToBase64String(new byte[] { 4, 5, 6 }))
        };
        
        _mockDatabase.Setup(x => x.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(serializedEvents);
        
        _mockSerializer.SetupSequence(x => x.Deserialize<IEvent>(It.IsAny<byte[]>()))
            .Returns(event1)
            .Returns(event2);

        // Act
        var events = await _eventStore.GetEventsAsync(aggregateId);

        // Assert
        Assert.Equal(2, events.Count);
        Assert.Equal(event1.OrderId, ((OrderCreatedEvent)events[0]).OrderId);
        Assert.Equal(event2.ItemName, ((OrderItemAddedEvent)events[1]).ItemName);
    }

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task GetEventsAsync_FromVersion_RetrievesSubset()
    {
        // Arrange
        var aggregateId = "order-123";
        var fromVersion = 5;
        
        _mockDatabase.Setup(x => x.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        await _eventStore.GetEventsAsync(aggregateId, fromVersion);

        // Assert
        _mockDatabase.Verify(x => x.ListRangeAsync(
            It.Is<RedisKey>(k => k.ToString().Contains(aggregateId)),
            It.Is<long>(v => v == fromVersion),
            It.Is<long>(v => v == -1),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task GetEventsAsync_NonExistentAggregate_ReturnsEmpty()
    {
        // Arrange
        var aggregateId = "non-existent";
        
        _mockDatabase.Setup(x => x.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        var events = await _eventStore.GetEventsAsync(aggregateId);

        // Assert
        Assert.Empty(events);
    }

    #endregion

    #region Snapshot Tests

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task SaveSnapshotAsync_Success()
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
        _mockDatabase.Setup(x => x.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _eventStore.SaveSnapshotAsync(aggregateId, snapshot, version);

        // Assert
        _mockDatabase.Verify(x => x.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString().Contains($"snapshot:{aggregateId}")),
            It.Is<RedisValue>(v => v.ToString().Contains(Convert.ToBase64String(serializedData))),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task GetLatestSnapshotAsync_ReturnsNewest()
    {
        // Arrange
        var aggregateId = "order-123";
        var snapshot = new OrderSnapshot
        {
            OrderId = aggregateId,
            TotalAmount = 500m,
            ItemCount = 5
        };
        var snapshotData = new
        {
            Version = 10,
            Data = Convert.ToBase64String(new byte[] { 1, 2, 3 })
        };
        var serializedSnapshot = System.Text.Json.JsonSerializer.Serialize(snapshotData);
        
        _mockDatabase.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(serializedSnapshot));
        
        _mockSerializer.Setup(x => x.Deserialize<OrderSnapshot>(It.IsAny<byte[]>()))
            .Returns(snapshot);

        // Act
        var result = await _eventStore.GetLatestSnapshotAsync<OrderSnapshot>(aggregateId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.Version);
        Assert.Equal(snapshot.OrderId, result.Snapshot.OrderId);
        Assert.Equal(snapshot.TotalAmount, result.Snapshot.TotalAmount);
    }

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task GetLatestSnapshotAsync_NoSnapshot_ReturnsNull()
    {
        // Arrange
        var aggregateId = "order-123";
        
        _mockDatabase.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _eventStore.GetLatestSnapshotAsync<OrderSnapshot>(aggregateId);

        // Assert
        Assert.Null(result);
    }

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task GetEventsAsync_WithSnapshot_OptimizedRetrieval()
    {
        // Arrange
        var aggregateId = "order-123";
        var snapshotVersion = 100;
        var snapshot = new OrderSnapshot { OrderId = aggregateId, TotalAmount = 1000m };
        
        var snapshotData = new
        {
            Version = snapshotVersion,
            Data = Convert.ToBase64String(new byte[] { 1, 2, 3 })
        };
        var serializedSnapshot = System.Text.Json.JsonSerializer.Serialize(snapshotData);
        
        _mockDatabase.Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(serializedSnapshot));
        
        _mockSerializer.Setup(x => x.Deserialize<OrderSnapshot>(It.IsAny<byte[]>()))
            .Returns(snapshot);
        
        _mockDatabase.Setup(x => x.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        var (retrievedSnapshot, events) = await _eventStore.GetEventsWithSnapshotAsync<OrderSnapshot>(aggregateId);

        // Assert
        Assert.NotNull(retrievedSnapshot);
        Assert.Equal(snapshotVersion, retrievedSnapshot.Version);
        
        // Verify only events after snapshot are retrieved
        _mockDatabase.Verify(x => x.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.Is<long>(v => v == snapshotVersion + 1),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion

    #region Error Handling

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task SaveAsync_ConnectionFailure_ThrowsException()
    {
        // Arrange
        var aggregateId = "order-123";
        var @event = new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m };
        
        _mockSerializer.Setup(x => x.Serialize(@event)).Returns(new byte[] { 1, 2, 3 });
        _mockDatabase.Setup(x => x.ListRightPushAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisConnectionException>(() =>
            _eventStore.SaveAsync(aggregateId, new[] { @event }));
    }

    [Fact(Skip = "RedisEventStore implementation pending")]
    public async Task GetEventsAsync_DeserializationFailure_SkipsInvalidEvent()
    {
        // Arrange
        var aggregateId = "order-123";
        var validEvent = new OrderCreatedEvent { OrderId = aggregateId, Amount = 100m };
        
        var serializedEvents = new[]
        {
            new RedisValue(Convert.ToBase64String(new byte[] { 1, 2, 3 })),
            new RedisValue("invalid-data"),
            new RedisValue(Convert.ToBase64String(new byte[] { 7, 8, 9 }))
        };
        
        _mockDatabase.Setup(x => x.ListRangeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(serializedEvents);
        
        _mockSerializer.SetupSequence(x => x.Deserialize<IEvent>(It.IsAny<byte[]>()))
            .Returns(validEvent)
            .Throws(new InvalidOperationException("Invalid data"))
            .Returns(validEvent);

        // Act
        var events = await _eventStore.GetEventsAsync(aggregateId);

        // Assert
        Assert.Equal(2, events.Count); // Should skip the invalid event
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


