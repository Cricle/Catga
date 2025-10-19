using Catga.Abstractions;
using Catga.Messages;
using Catga.Outbox;
using Catga.Persistence.Redis.Persistence;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Catga.Tests.Persistence;

/// <summary>
/// Redis Outbox Store 测试
/// 测试 Outbox 模式的消息存储、处理状态管理、清理机制
/// </summary>
public class RedisOutboxStoreTests : IAsyncLifetime
{
    private readonly Mock<IConnectionMultiplexer> _mockConnection;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<IMessageSerializer> _mockSerializer;
    private RedisOutboxPersistence _outboxStore = null!;

    public RedisOutboxStoreTests()
    {
        _mockConnection = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockSerializer = new Mock<IMessageSerializer>();

        _mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);
    }

    public Task InitializeAsync()
    {
        _outboxStore = new RedisOutboxPersistence(_mockConnection.Object, _mockSerializer.Object);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _outboxStore?.Dispose();
        return Task.CompletedTask;
    }

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_SingleMessage_Success()
    {
        // Arrange
        var message = new OrderCreatedEvent
        {
            OrderId = "order-123",
            Amount = 100.50m,
            OccurredAt = DateTime.UtcNow
        };
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid().ToString(),
            MessageType = message.GetType().FullName!,
            Payload = new byte[] { 1, 2, 3 },
            CreatedAt = DateTime.UtcNow,
            Status = OutboxMessageStatus.Pending
        };
        
        _mockSerializer.Setup(x => x.Serialize(message)).Returns(outboxMessage.Payload);
        _mockDatabase.Setup(x => x.SortedSetAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _outboxStore.AddAsync(message);

        // Assert
        _mockDatabase.Verify(x => x.SortedSetAddAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("outbox:pending")),
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()), Times.Once);
        
        _mockSerializer.Verify(x => x.Serialize(message), Times.Once);
    }

    [Fact]
    public async Task AddAsync_BatchMessages_Success()
    {
        // Arrange
        var messages = new IMessage[]
        {
            new OrderCreatedEvent { OrderId = "order-1", Amount = 100m },
            new OrderCreatedEvent { OrderId = "order-2", Amount = 200m },
            new OrderCreatedEvent { OrderId = "order-3", Amount = 300m }
        };
        
        _mockSerializer.Setup(x => x.Serialize(It.IsAny<IMessage>()))
            .Returns(new byte[] { 1, 2, 3 });
        
        _mockDatabase.Setup(x => x.SortedSetAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<SortedSetEntry[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(3);

        // Act
        await _outboxStore.AddBatchAsync(messages);

        // Assert
        _mockDatabase.Verify(x => x.SortedSetAddAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("outbox:pending")),
            It.Is<SortedSetEntry[]>(arr => arr.Length == 3),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion

    #region GetPendingAsync Tests

    [Fact]
    public async Task GetPendingAsync_RetrievesUnprocessedMessages()
    {
        // Arrange
        var message1 = new OutboxMessage
        {
            Id = "msg-1",
            MessageType = typeof(OrderCreatedEvent).FullName!,
            Payload = new byte[] { 1, 2, 3 },
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            Status = OutboxMessageStatus.Pending
        };
        
        var serializedMessages = new[]
        {
            new SortedSetEntry(
                System.Text.Json.JsonSerializer.Serialize(message1),
                message1.CreatedAt.Ticks)
        };
        
        _mockDatabase.Setup(x => x.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<Order>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(serializedMessages);

        // Act
        var pending = await _outboxStore.GetPendingAsync(batchSize: 10);

        // Assert
        Assert.Single(pending);
        Assert.Equal(message1.Id, pending[0].Id);
        Assert.Equal(OutboxMessageStatus.Pending, pending[0].Status);
    }

    [Fact]
    public async Task GetPendingAsync_WithBatchSize_LimitsResults()
    {
        // Arrange
        var batchSize = 5;
        var serializedMessages = Enumerable.Range(0, 10)
            .Select(i => new SortedSetEntry(
                System.Text.Json.JsonSerializer.Serialize(new OutboxMessage
                {
                    Id = $"msg-{i}",
                    MessageType = "TestMessage",
                    Payload = new byte[] { 1, 2, 3 },
                    CreatedAt = DateTime.UtcNow,
                    Status = OutboxMessageStatus.Pending
                }),
                i))
            .ToArray();
        
        _mockDatabase.Setup(x => x.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<Order>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(serializedMessages.Take(batchSize).ToArray());

        // Act
        var pending = await _outboxStore.GetPendingAsync(batchSize);

        // Assert
        Assert.Equal(batchSize, pending.Count);
        
        _mockDatabase.Verify(x => x.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<Order>(),
            It.Is<long>(skip => skip == 0),
            It.Is<long>(take => take == batchSize),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetPendingAsync_EmptyOutbox_ReturnsEmpty()
    {
        // Arrange
        _mockDatabase.Setup(x => x.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<Order>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<SortedSetEntry>());

        // Act
        var pending = await _outboxStore.GetPendingAsync();

        // Assert
        Assert.Empty(pending);
    }

    #endregion

    #region MarkAsProcessedAsync Tests

    [Fact]
    public async Task MarkAsProcessedAsync_UpdatesStatus()
    {
        // Arrange
        var messageId = "msg-123";
        
        _mockDatabase.Setup(x => x.SortedSetRemoveAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        
        _mockDatabase.Setup(x => x.SortedSetAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _outboxStore.MarkAsProcessedAsync(messageId);

        // Assert
        _mockDatabase.Verify(x => x.SortedSetRemoveAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("outbox:pending")),
            It.Is<RedisValue>(v => v.ToString().Contains(messageId)),
            It.IsAny<CommandFlags>()), Times.Once);
        
        _mockDatabase.Verify(x => x.SortedSetAddAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("outbox:processed")),
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task MarkAsProcessedAsync_BatchMessages_Success()
    {
        // Arrange
        var messageIds = new[] { "msg-1", "msg-2", "msg-3" };
        
        _mockDatabase.Setup(x => x.SortedSetRemoveAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(3);
        
        _mockDatabase.Setup(x => x.SortedSetAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<SortedSetEntry[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(3);

        // Act
        await _outboxStore.MarkAsProcessedBatchAsync(messageIds);

        // Assert
        _mockDatabase.Verify(x => x.SortedSetRemoveAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("outbox:pending")),
            It.Is<RedisValue[]>(arr => arr.Length == 3),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion

    #region MarkAsFailedAsync Tests

    [Fact]
    public async Task MarkAsFailedAsync_UpdatesStatusWithError()
    {
        // Arrange
        var messageId = "msg-123";
        var error = "Connection timeout";
        
        _mockDatabase.Setup(x => x.SortedSetRemoveAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        
        _mockDatabase.Setup(x => x.SortedSetAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _outboxStore.MarkAsFailedAsync(messageId, error);

        // Assert
        _mockDatabase.Verify(x => x.SortedSetAddAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("outbox:failed")),
            It.Is<RedisValue>(v => v.ToString().Contains(error)),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task MarkAsFailedAsync_WithRetry_IncrementsRetryCount()
    {
        // Arrange
        var messageId = "msg-123";
        var error = "Temporary failure";
        var retryAfter = TimeSpan.FromMinutes(5);
        
        _mockDatabase.Setup(x => x.SortedSetRemoveAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        
        _mockDatabase.Setup(x => x.SortedSetAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _outboxStore.MarkAsFailedAsync(messageId, error, retryAfter);

        // Assert
        _mockDatabase.Verify(x => x.SortedSetAddAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("outbox:pending")),
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    #endregion

    #region CleanupAsync Tests

    [Fact]
    public async Task CleanupAsync_RemovesOldProcessedMessages()
    {
        // Arrange
        var olderThan = TimeSpan.FromDays(7);
        var cutoffTime = DateTime.UtcNow.Subtract(olderThan);
        
        _mockDatabase.Setup(x => x.SortedSetRemoveRangeByScoreAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(42);

        // Act
        var removed = await _outboxStore.CleanupAsync(olderThan);

        // Assert
        Assert.Equal(42, removed);
        
        _mockDatabase.Verify(x => x.SortedSetRemoveRangeByScoreAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("outbox:processed")),
            It.IsAny<double>(),
            It.Is<double>(score => score <= cutoffTime.Ticks),
            It.IsAny<Exclude>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task CleanupAsync_RemovesOldFailedMessages()
    {
        // Arrange
        var olderThan = TimeSpan.FromDays(30);
        
        _mockDatabase.Setup(x => x.SortedSetRemoveRangeByScoreAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(10);

        // Act
        await _outboxStore.CleanupAsync(olderThan);

        // Assert
        _mockDatabase.Verify(x => x.SortedSetRemoveRangeByScoreAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("outbox:processed") || 
                                  k.ToString().Contains("outbox:failed")),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<CommandFlags>()), Times.AtLeastOnce);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task ConcurrentAddAsync_ThreadSafe()
    {
        // Arrange
        var messages = Enumerable.Range(0, 100)
            .Select(i => new OrderCreatedEvent
            {
                OrderId = $"order-{i}",
                Amount = 100m * i
            })
            .ToArray();
        
        _mockSerializer.Setup(x => x.Serialize(It.IsAny<IMessage>()))
            .Returns(new byte[] { 1, 2, 3 });
        
        _mockDatabase.Setup(x => x.SortedSetAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var tasks = messages.Select(msg => _outboxStore.AddAsync(msg));
        await Task.WhenAll(tasks);

        // Assert
        _mockDatabase.Verify(x => x.SortedSetAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()), Times.Exactly(100));
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task AddAsync_ConnectionFailure_ThrowsException()
    {
        // Arrange
        var message = new OrderCreatedEvent { OrderId = "order-123", Amount = 100m };
        
        _mockSerializer.Setup(x => x.Serialize(message)).Returns(new byte[] { 1, 2, 3 });
        _mockDatabase.Setup(x => x.SortedSetAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisConnectionException>(() =>
            _outboxStore.AddAsync(message));
    }

    [Fact]
    public async Task GetPendingAsync_DeserializationFailure_SkipsInvalidMessage()
    {
        // Arrange
        var serializedMessages = new[]
        {
            new SortedSetEntry("valid-json-1", 1),
            new SortedSetEntry("invalid-json", 2),
            new SortedSetEntry("valid-json-2", 3)
        };
        
        _mockDatabase.Setup(x => x.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<Order>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(serializedMessages);

        // Act
        var pending = await _outboxStore.GetPendingAsync();

        // Assert
        // Should skip invalid messages and only return valid ones
        Assert.True(pending.Count <= 2);
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

    #endregion
}

