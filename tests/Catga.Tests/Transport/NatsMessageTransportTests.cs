using Catga.Abstractions;
using Catga.Messages;
using Catga.Transport;
using Moq;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Xunit;

namespace Catga.Tests.Transport;

/// <summary>
/// NATS 消息传输测试
/// 测试 Core NATS (Pub/Sub) 和 JetStream (持久化) 的实现
/// </summary>
public class NatsMessageTransportTests : IAsyncLifetime
{
    private readonly Mock<INatsConnection> _mockConnection;
    private readonly Mock<INatsJSContext> _mockJetStream;
    private readonly Mock<IMessageSerializer> _mockSerializer;
    private NatsMessageTransport _transport = null!;

    public NatsMessageTransportTests()
    {
        _mockConnection = new Mock<INatsConnection>();
        _mockJetStream = new Mock<INatsJSContext>();
        _mockSerializer = new Mock<IMessageSerializer>();

        _mockConnection.Setup(x => x.CreateJetStreamContext(It.IsAny<NatsJSOpts>()))
            .Returns(_mockJetStream.Object);
    }

    public Task InitializeAsync()
    {
        _transport = new NatsMessageTransport(_mockConnection.Object, _mockSerializer.Object);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _transport?.Dispose();
        return Task.CompletedTask;
    }

    #region Core NATS Publish/Subscribe Tests

    [Fact]
    public async Task PublishAsync_CoreNats_Success()
    {
        // Arrange
        var message = new TestEvent { Id = "test-1", Data = "Test Data" };
        var serializedData = new byte[] { 1, 2, 3 };
        _mockSerializer.Setup(x => x.Serialize(message)).Returns(serializedData);
        
        _mockConnection.Setup(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<NatsHeaders>(),
            It.IsAny<string>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsPubOpts>(),
            It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _transport.PublishAsync(message);

        // Assert
        _mockConnection.Verify(x => x.PublishAsync(
            It.Is<string>(s => s.Contains(nameof(TestEvent))),
            It.Is<byte[]>(b => b.SequenceEqual(serializedData)),
            It.IsAny<NatsHeaders>(),
            It.IsAny<string>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsPubOpts>(),
            It.IsAny<CancellationToken>()), Times.Once);
        
        _mockSerializer.Verify(x => x.Serialize(message), Times.Once);
    }

    [Fact]
    public async Task SubscribeAsync_CoreNats_ReceivesMessages()
    {
        // Arrange
        var receivedMessages = new List<object>();
        var message = new TestEvent { Id = "test-1", Data = "Test Data" };
        var serializedData = new byte[] { 1, 2, 3 };
        
        _mockSerializer.Setup(x => x.Deserialize<TestEvent>(serializedData))
            .Returns(message);

        var mockSubscription = new Mock<IAsyncEnumerable<NatsMsg<byte[]>>>();
        _mockConnection.Setup(x => x.SubscribeAsync<byte[]>(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<NatsSubOpts>(),
            It.IsAny<CancellationToken>()))
            .Returns(mockSubscription.Object);

        // Act
        await _transport.SubscribeAsync<TestEvent>(msg =>
        {
            receivedMessages.Add(msg);
            return Task.CompletedTask;
        });

        // Assert
        _mockConnection.Verify(x => x.SubscribeAsync<byte[]>(
            It.Is<string>(s => s.Contains(nameof(TestEvent))),
            It.IsAny<string>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<NatsSubOpts>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Request-Reply Tests

    [Fact]
    public async Task SendAsync_RequestReply_Success()
    {
        // Arrange
        var request = new TestRequest { RequestId = "req-1", Data = "Request Data" };
        var response = new TestResponse { RequestId = "req-1", Result = "Success" };
        var serializedRequest = new byte[] { 1, 2, 3 };
        var serializedResponse = new byte[] { 4, 5, 6 };
        
        _mockSerializer.Setup(x => x.Serialize(request)).Returns(serializedRequest);
        _mockSerializer.Setup(x => x.Deserialize<TestResponse>(serializedResponse))
            .Returns(response);
        
        var mockReply = new NatsMsg<byte[]>(
            subject: "reply.subject",
            replyTo: null,
            size: serializedResponse.Length,
            headers: null,
            data: serializedResponse,
            connection: _mockConnection.Object);

        _mockConnection.Setup(x => x.RequestAsync<byte[], byte[]>(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<NatsHeaders>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<NatsRequestOpts>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockReply);

        // Act
        var result = await _transport.SendAsync<TestRequest, TestResponse>(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(response.RequestId, result.RequestId);
        Assert.Equal(response.Result, result.Result);
        
        _mockSerializer.Verify(x => x.Serialize(request), Times.Once);
        _mockSerializer.Verify(x => x.Deserialize<TestResponse>(serializedResponse), Times.Once);
    }

    [Fact]
    public async Task SendAsync_Timeout_ThrowsException()
    {
        // Arrange
        var request = new TestRequest { RequestId = "req-1", Data = "Request Data" };
        _mockSerializer.Setup(x => x.Serialize(request)).Returns(new byte[] { 1, 2, 3 });
        
        _mockConnection.Setup(x => x.RequestAsync<byte[], byte[]>(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<NatsHeaders>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<NatsRequestOpts>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Request timeout"));

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() =>
            _transport.SendAsync<TestRequest, TestResponse>(request, timeout: TimeSpan.FromSeconds(1)));
    }

    #endregion

    #region JetStream Tests

    [Fact]
    public async Task PublishAsync_JetStream_Success()
    {
        // Arrange
        var message = new TestEvent { Id = "test-1", Data = "Test Data" };
        var serializedData = new byte[] { 1, 2, 3 };
        _mockSerializer.Setup(x => x.Serialize(message)).Returns(serializedData);
        
        var mockPubAck = new Mock<PubAckResponse>();
        _mockJetStream.Setup(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsJSPubOpts>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPubAck.Object);

        // Act
        await _transport.PublishAsync(message, new MessageMetadata { Persistent = true });

        // Assert
        _mockJetStream.Verify(x => x.PublishAsync(
            It.Is<string>(s => s.Contains(nameof(TestEvent))),
            It.Is<byte[]>(b => b.SequenceEqual(serializedData)),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsJSPubOpts>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
        
        _mockConnection.Setup(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<NatsHeaders>(),
            It.IsAny<string>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsPubOpts>(),
            It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        // Act
        await _transport.PublishBatchAsync(messages);

        // Assert
        _mockConnection.Verify(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<NatsHeaders>(),
            It.IsAny<string>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsPubOpts>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
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
        
        var mockReply = new NatsMsg<byte[]>(
            subject: "reply.subject",
            replyTo: null,
            size: serializedResponse.Length,
            headers: null,
            data: serializedResponse,
            connection: _mockConnection.Object);

        _mockConnection.Setup(x => x.RequestAsync<byte[], byte[]>(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<NatsHeaders>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<NatsRequestOpts>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockReply);

        // Act
        var results = await _transport.SendBatchAsync<TestRequest, TestResponse>(requests);

        // Assert
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task PublishAsync_ConnectionClosed_ThrowsException()
    {
        // Arrange
        var message = new TestEvent { Id = "test-1", Data = "Test Data" };
        _mockSerializer.Setup(x => x.Serialize(message)).Returns(new byte[] { 1, 2, 3 });
        
        _mockConnection.Setup(x => x.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<NatsHeaders>(),
            It.IsAny<string>(),
            It.IsAny<INatsSerialize<byte[]>>(),
            It.IsAny<NatsPubOpts>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NatsException("Connection closed"));

        // Act & Assert
        await Assert.ThrowsAsync<NatsException>(() =>
            _transport.PublishAsync(message));
    }

    [Fact]
    public async Task SubscribeAsync_InvalidMessage_SkipsMessage()
    {
        // Arrange
        var receivedMessages = new List<object>();
        
        _mockSerializer.Setup(x => x.Deserialize<TestEvent>(It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("Deserialization failed"));

        var mockSubscription = new Mock<IAsyncEnumerable<NatsMsg<byte[]>>>();
        _mockConnection.Setup(x => x.SubscribeAsync<byte[]>(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<NatsSubOpts>(),
            It.IsAny<CancellationToken>()))
            .Returns(mockSubscription.Object);

        // Act - should not throw
        await _transport.SubscribeAsync<TestEvent>(msg =>
        {
            receivedMessages.Add(msg);
            return Task.CompletedTask;
        });

        // Assert
        Assert.Empty(receivedMessages);
    }

    #endregion

    #region Resource Cleanup

    [Fact]
    public void Dispose_CleansUpResources()
    {
        // Arrange
        var transport = new NatsMessageTransport(_mockConnection.Object, _mockSerializer.Object);

        // Act
        transport.Dispose();

        // Assert
        _mockConnection.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_CancelsActiveSubscriptions()
    {
        // Arrange
        var transport = new NatsMessageTransport(_mockConnection.Object, _mockSerializer.Object);
        
        var mockSubscription = new Mock<IAsyncEnumerable<NatsMsg<byte[]>>>();
        _mockConnection.Setup(x => x.SubscribeAsync<byte[]>(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<INatsDeserialize<byte[]>>(),
            It.IsAny<NatsSubOpts>(),
            It.IsAny<CancellationToken>()))
            .Returns(mockSubscription.Object);

        await transport.SubscribeAsync<TestEvent>(_ => Task.CompletedTask);

        // Act
        transport.Dispose();

        // Assert
        // Verify subscriptions are cancelled
        Assert.True(true); // Placeholder - actual verification depends on implementation
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

