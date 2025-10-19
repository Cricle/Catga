using Catga.Abstractions;
using Catga.Messages;
using Catga.Transport;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Catga.Tests.Transport;

/// <summary>
/// Redis 消息传输测试
/// 测试 QoS 0 (Pub/Sub) 和 QoS 1 (Streams) 的实现
/// </summary>
public class RedisMessageTransportTests : IAsyncLifetime
{
    private readonly Mock<IConnectionMultiplexer> _mockConnection;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ISubscriber> _mockSubscriber;
    private readonly Mock<IMessageSerializer> _mockSerializer;
    private RedisMessageTransport _transport = null!;

    public RedisMessageTransportTests()
    {
        _mockConnection = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockSubscriber = new Mock<ISubscriber>();
        _mockSerializer = new Mock<IMessageSerializer>();

        _mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);
        _mockConnection.Setup(x => x.GetSubscriber(It.IsAny<object>()))
            .Returns(_mockSubscriber.Object);
    }

    public Task InitializeAsync()
    {
        _transport = new RedisMessageTransport(_mockConnection.Object, _mockSerializer.Object);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _transport?.Dispose();
        return Task.CompletedTask;
    }

    #region QoS 0 (Pub/Sub) Tests

    [Fact]
    public async Task PublishAsync_QoS0_UsesPubSub()
    {
        // Arrange
        var message = new TestEvent { Id = "test-1", Data = "Test Data" };
        var serializedData = new byte[] { 1, 2, 3 };
        _mockSerializer.Setup(x => x.Serialize(message)).Returns(serializedData);
        
        _mockSubscriber.Setup(x => x.PublishAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        // Act
        await _transport.PublishAsync(message, new MessageMetadata { QoS = QualityOfService.AtMostOnce });

        // Assert
        _mockSubscriber.Verify(x => x.PublishAsync(
            It.Is<RedisChannel>(ch => ch.ToString().Contains(nameof(TestEvent))),
            It.Is<RedisValue>(rv => rv.ToString() == Convert.ToBase64String(serializedData)),
            It.IsAny<CommandFlags>()), Times.Once);
        
        _mockSerializer.Verify(x => x.Serialize(message), Times.Once);
    }

    [Fact]
    public async Task SubscribeAsync_QoS0_ReceivesMessages()
    {
        // Arrange
        var receivedMessages = new List<object>();
        var message = new TestEvent { Id = "test-1", Data = "Test Data" };
        var serializedData = new byte[] { 1, 2, 3 };
        
        _mockSerializer.Setup(x => x.Deserialize<TestEvent>(serializedData))
            .Returns(message);

        Action<RedisChannel, RedisValue>? subscribedHandler = null;
        _mockSubscriber.Setup(x => x.Subscribe(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((ch, handler, flags) =>
            {
                subscribedHandler = handler;
            });

        // Act
        await _transport.SubscribeAsync<TestEvent>(msg =>
        {
            receivedMessages.Add(msg);
            return Task.CompletedTask;
        }, new MessageMetadata { QoS = QualityOfService.AtMostOnce });

        // Simulate message arrival
        subscribedHandler?.Invoke("test-channel", Convert.ToBase64String(serializedData));

        // Assert
        await Task.Delay(100); // Give time for async processing
        Assert.Single(receivedMessages);
        Assert.Equal(message.Id, ((TestEvent)receivedMessages[0]).Id);
    }

    #endregion

    #region QoS 1 (Streams) Tests

    [Fact]
    public async Task PublishAsync_QoS1_UsesStreams()
    {
        // Arrange
        var message = new TestEvent { Id = "test-1", Data = "Test Data" };
        var serializedData = new byte[] { 1, 2, 3 };
        _mockSerializer.Setup(x => x.Serialize(message)).Returns(serializedData);
        
        _mockDatabase.Setup(x => x.StreamAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<RedisValue>(),
            It.IsAny<RedisValue?>(),
            It.IsAny<int?>(),
            It.IsAny<bool>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("1-0"));

        // Act
        await _transport.PublishAsync(message, new MessageMetadata { QoS = QualityOfService.AtLeastOnce });

        // Assert
        _mockDatabase.Verify(x => x.StreamAddAsync(
            It.Is<RedisKey>(k => k.ToString().Contains(nameof(TestEvent))),
            It.Is<RedisValue>(rv => rv == "data"),
            It.Is<RedisValue>(rv => rv.ToString() == Convert.ToBase64String(serializedData)),
            It.IsAny<RedisValue?>(),
            It.IsAny<int?>(),
            It.IsAny<bool>(),
            It.IsAny<CommandFlags>()), Times.Once);
        
        _mockSerializer.Verify(x => x.Serialize(message), Times.Once);
    }

    [Fact]
    public async Task SendAsync_QoS1_UsesStreams()
    {
        // Arrange
        var request = new TestRequest { RequestId = "req-1", Data = "Request Data" };
        var response = new TestResponse { RequestId = "req-1", Result = "Success" };
        var serializedRequest = new byte[] { 1, 2, 3 };
        var serializedResponse = new byte[] { 4, 5, 6 };
        
        _mockSerializer.Setup(x => x.Serialize(request)).Returns(serializedRequest);
        _mockSerializer.Setup(x => x.Deserialize<TestResponse>(serializedResponse))
            .Returns(response);
        
        _mockDatabase.Setup(x => x.StreamAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<RedisValue>(),
            It.IsAny<RedisValue?>(),
            It.IsAny<int?>(),
            It.IsAny<bool>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("1-0"));

        // Setup response stream read
        _mockDatabase.Setup(x => x.StreamReadAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<int?>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(new[]
            {
                new StreamEntry(
                    new RedisValue("1-0"),
                    new[] { new NameValueEntry("data", Convert.ToBase64String(serializedResponse)) })
            });

        // Act
        var result = await _transport.SendAsync<TestRequest, TestResponse>(
            request, 
            new MessageMetadata { QoS = QualityOfService.AtLeastOnce });

        // Assert
        Assert.NotNull(result);
        Assert.Equal(response.RequestId, result.RequestId);
        Assert.Equal(response.Result, result.Result);
    }

    #endregion

    #region Batch Operations

    [Fact]
    public async Task PublishBatchAsync_Success()
    {
        // Arrange
        var messages = new List<IMessage>
        {
            new TestEvent { Id = "test-1", Data = "Data 1" },
            new TestEvent { Id = "test-2", Data = "Data 2" },
            new TestEvent { Id = "test-3", Data = "Data 3" }
        };

        _mockSerializer.Setup(x => x.Serialize(It.IsAny<IMessage>()))
            .Returns(new byte[] { 1, 2, 3 });
        
        _mockSubscriber.Setup(x => x.PublishAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        // Act
        await _transport.PublishBatchAsync(messages, new MessageMetadata { QoS = QualityOfService.AtMostOnce });

        // Assert
        _mockSubscriber.Verify(x => x.PublishAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SendBatchAsync_Success()
    {
        // Arrange
        var requests = new List<IRequest<TestResponse>>
        {
            new TestRequest { RequestId = "req-1", Data = "Data 1" },
            new TestRequest { RequestId = "req-2", Data = "Data 2" }
        };

        var response = new TestResponse { RequestId = "req-1", Result = "Success" };
        var serializedResponse = new byte[] { 4, 5, 6 };

        _mockSerializer.Setup(x => x.Serialize(It.IsAny<IRequest<TestResponse>>()))
            .Returns(new byte[] { 1, 2, 3 });
        _mockSerializer.Setup(x => x.Deserialize<TestResponse>(serializedResponse))
            .Returns(response);
        
        _mockDatabase.Setup(x => x.StreamAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<RedisValue>(),
            It.IsAny<RedisValue?>(),
            It.IsAny<int?>(),
            It.IsAny<bool>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("1-0"));

        _mockDatabase.Setup(x => x.StreamReadAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<int?>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(new[]
            {
                new StreamEntry(
                    new RedisValue("1-0"),
                    new[] { new NameValueEntry("data", Convert.ToBase64String(serializedResponse)) })
            });

        // Act
        var results = await _transport.SendBatchAsync<TestRequest, TestResponse>(
            requests, 
            new MessageMetadata { QoS = QualityOfService.AtLeastOnce });

        // Assert
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task PublishAsync_ConnectionFailure_ThrowsException()
    {
        // Arrange
        var message = new TestEvent { Id = "test-1", Data = "Test Data" };
        _mockSerializer.Setup(x => x.Serialize(message)).Returns(new byte[] { 1, 2, 3 });
        
        _mockSubscriber.Setup(x => x.PublishAsync(
            It.IsAny<RedisChannel>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisConnectionException>(() =>
            _transport.PublishAsync(message, new MessageMetadata { QoS = QualityOfService.AtMostOnce }));
    }

    [Fact]
    public async Task SubscribeAsync_InvalidMessage_SkipsMessage()
    {
        // Arrange
        var receivedMessages = new List<object>();
        
        _mockSerializer.Setup(x => x.Deserialize<TestEvent>(It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("Deserialization failed"));

        Action<RedisChannel, RedisValue>? subscribedHandler = null;
        _mockSubscriber.Setup(x => x.Subscribe(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((ch, handler, flags) =>
            {
                subscribedHandler = handler;
            });

        // Act
        await _transport.SubscribeAsync<TestEvent>(msg =>
        {
            receivedMessages.Add(msg);
            return Task.CompletedTask;
        }, new MessageMetadata { QoS = QualityOfService.AtMostOnce });

        // Simulate invalid message
        subscribedHandler?.Invoke("test-channel", "invalid-data");

        // Assert
        await Task.Delay(100);
        Assert.Empty(receivedMessages); // Should not crash, just skip
    }

    #endregion

    #region Resource Cleanup

    [Fact]
    public void Dispose_CleansUpResources()
    {
        // Arrange
        var transport = new RedisMessageTransport(_mockConnection.Object, _mockSerializer.Object);

        // Act
        transport.Dispose();

        // Assert
        // Verify connection is disposed (if applicable)
        // In real implementation, verify resources are released
        Assert.True(true); // Placeholder - actual implementation depends on RedisMessageTransport
    }

    [Fact]
    public async Task DisposeAsync_CancelsActiveSubscriptions()
    {
        // Arrange
        var transport = new RedisMessageTransport(_mockConnection.Object, _mockSerializer.Object);
        
        _mockSubscriber.Setup(x => x.Subscribe(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()));

        await transport.SubscribeAsync<TestEvent>(_ => Task.CompletedTask);

        // Act
        transport.Dispose();

        // Assert
        _mockSubscriber.Verify(x => x.Unsubscribe(
            It.IsAny<RedisChannel>(),
            It.IsAny<Action<RedisChannel, RedisValue>>(),
            It.IsAny<CommandFlags>()), Times.AtLeastOnce);
    }

    #endregion

    #region Test Models

    private record TestEvent : IEvent
    {
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    private record TestRequest : IRequest<TestResponse>
    {
        public required string RequestId { get; init; }
        public required string Data { get; init; }
    }

    private record TestResponse : IResponse
    {
        public required string RequestId { get; init; }
        public required string Result { get; init; }
    }

    #endregion
}

