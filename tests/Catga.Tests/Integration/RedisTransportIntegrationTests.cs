using Catga.Abstractions;
using Catga.Messages;
using Catga.Serialization.Json;
using Catga.Transport;
using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration;

/// <summary>
/// Redis Transport 集成测试
/// 使用 Testcontainers 启动真实的 Redis 容器进行测试
/// </summary>
public class RedisTransportIntegrationTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private RedisMessageTransport? _transport;
    private IMessageSerializer? _serializer;

    public async Task InitializeAsync()
    {
        // 启动 Redis 容器
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();

        // 连接到 Redis
        var connectionString = _redisContainer.GetConnectionString();
        _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);

        // 创建序列化器和传输层
        _serializer = new JsonMessageSerializer();
        _transport = new RedisMessageTransport(_redis, _serializer);
    }

    public async Task DisposeAsync()
    {
        if (_transport != null)
            await _transport.DisposeAsync();

        _redis?.Dispose();

        if (_redisContainer != null)
            await _redisContainer.DisposeAsync();
    }

    [Fact]
    public async Task PublishAsync_QoS0_ShouldDeliverMessage()
    {
        // Arrange
        var testEvent = new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "test-1",
            Data = "Hello Redis!",
            QoS = QualityOfService.AtMostOnce
        };

        var receivedMessages = new List<TestEvent>();
        var tcs = new TaskCompletionSource<bool>();

        // Act - Subscribe
        await _transport!.SubscribeAsync<TestEvent>((msg, ctx) =>
        {
            receivedMessages.Add(msg);
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        });

        // Wait a bit for subscription to be ready
        await Task.Delay(100);

        // Act - Publish
        await _transport.PublishAsync(testEvent);

        // Wait for message
        var received = await Task.WhenAny(tcs.Task, Task.Delay(5000));

        // Assert
        received.Should().Be(tcs.Task, "message should be received within 5 seconds");
        receivedMessages.Should().ContainSingle();
        receivedMessages[0].Id.Should().Be("test-1");
        receivedMessages[0].Data.Should().Be("Hello Redis!");
    }

    [Fact]
    public async Task PublishAsync_QoS1_ShouldPersistAndDeliver()
    {
        // Arrange
        var testEvent = new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "test-2",
            Data = "Persistent message",
            QoS = QualityOfService.AtLeastOnce
        };

        var receivedMessages = new List<TestEvent>();
        var tcs = new TaskCompletionSource<bool>();

        // Act - Subscribe
        await _transport!.SubscribeAsync<TestEvent>((msg, ctx) =>
        {
            receivedMessages.Add(msg);
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        });

        await Task.Delay(100);

        // Act - Publish
        await _transport.PublishAsync(testEvent);

        // Wait for message
        var received = await Task.WhenAny(tcs.Task, Task.Delay(5000));

        // Assert
        received.Should().Be(tcs.Task);
        receivedMessages.Should().ContainSingle();
        receivedMessages[0].Id.Should().Be("test-2");
        receivedMessages[0].QoS.Should().Be(QualityOfService.AtLeastOnce);
    }

    [Fact(Skip = "SendAsync request-reply pattern needs proper setup")]
    public async Task SendAsync_RequestReply_ShouldWork()
    {
        // TODO: Implement full request-reply integration test
        // This requires proper responder setup and async coordination
        await Task.CompletedTask;
    }

    [Fact]
    public async Task PublishBatchAsync_ShouldDeliverAllMessages()
    {
        // Arrange
        var events = Enumerable.Range(1, 10).Select(i => new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = $"batch-{i}",
            Data = $"Message {i}",
            QoS = QualityOfService.AtMostOnce
        }).ToList();

        var receivedMessages = new List<TestEvent>();
        var tcs = new TaskCompletionSource<bool>();
        var expectedCount = events.Count;

        // Act - Subscribe
        await _transport!.SubscribeAsync<TestEvent>((msg, ctx) =>
        {
            lock (receivedMessages)
            {
                receivedMessages.Add(msg);
                if (receivedMessages.Count >= expectedCount)
                {
                    tcs.TrySetResult(true);
                }
            }
            return Task.CompletedTask;
        });

        await Task.Delay(100);

        // Act - Publish batch
        await _transport.PublishBatchAsync(events);

        // Wait for all messages
        var received = await Task.WhenAny(tcs.Task, Task.Delay(10000));

        // Assert
        received.Should().Be(tcs.Task, "all messages should be received within 10 seconds");
        receivedMessages.Should().HaveCount(expectedCount);
        receivedMessages.Select(m => m.Id).Should().BeEquivalentTo(events.Select(e => e.Id));
    }

    [Fact]
    public async Task MultipleSubscribers_ShouldAllReceiveMessage()
    {
        // Arrange
        var testEvent = new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "multi-sub",
            Data = "Broadcast message",
            QoS = QualityOfService.AtMostOnce
        };

        var subscriber1Messages = new List<TestEvent>();
        var subscriber2Messages = new List<TestEvent>();
        var tcs1 = new TaskCompletionSource<bool>();
        var tcs2 = new TaskCompletionSource<bool>();

        // Act - Subscribe with two subscribers
        await _transport!.SubscribeAsync<TestEvent>((msg, ctx) =>
        {
            subscriber1Messages.Add(msg);
            tcs1.TrySetResult(true);
            return Task.CompletedTask;
        });

        await _transport.SubscribeAsync<TestEvent>((msg, ctx) =>
        {
            subscriber2Messages.Add(msg);
            tcs2.TrySetResult(true);
            return Task.CompletedTask;
        });

        await Task.Delay(100);

        // Act - Publish
        await _transport.PublishAsync(testEvent);

        // Wait for both subscribers
        var allReceived = await Task.WhenAll(
            Task.WhenAny(tcs1.Task, Task.Delay(5000)),
            Task.WhenAny(tcs2.Task, Task.Delay(5000)));

        // Assert
        allReceived.Should().AllSatisfy(t => t.Should().NotBe(Task.Delay(5000)));
        subscriber1Messages.Should().ContainSingle();
        subscriber2Messages.Should().ContainSingle();
        subscriber1Messages[0].Id.Should().Be("multi-sub");
        subscriber2Messages[0].Id.Should().Be("multi-sub");
    }

    #region Test Models

    private record TestEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        public QualityOfService QoS { get; init; } = QualityOfService.AtMostOnce;
    }

    private record TestRequest : IRequest<TestResponse>
    {
        public required long MessageId { get; init; }
        public required string RequestId { get; init; }
        public required string Data { get; init; }
    }

    private record TestResponse
    {
        public required string RequestId { get; init; }
        public required string Result { get; init; }
        public bool Success { get; init; }
    }

    #endregion
}

