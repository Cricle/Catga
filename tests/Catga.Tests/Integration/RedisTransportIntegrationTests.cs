using Catga.Abstractions;
using Catga.Core;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using FluentAssertions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;
using MemoryPack;

namespace Catga.Tests.Integration;

/// <summary>
/// Redis Transport 集成测试
/// 使用 Testcontainers 启动真实的 Redis 容器进行测试
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public partial class RedisTransportIntegrationTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private RedisMessageTransport? _transport;
    private IMessageSerializer? _serializer;

    public async Task InitializeAsync()
    {
        // 启动 Redis 容器
        var redisImage = Environment.GetEnvironmentVariable("TEST_REDIS_IMAGE") ?? "redis:7-alpine";
        _redisContainer = new RedisBuilder()
            .WithImage(redisImage)
            .Build();

        await _redisContainer.StartAsync();

        // 连接到 Redis
        var connectionString = _redisContainer.GetConnectionString();
        _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);

        // 创建序列化器和传输层
        _serializer = new MemoryPackMessageSerializer();
        _transport = new RedisMessageTransport(_redis, _serializer, provider: new Catga.Resilience.DiagnosticResiliencePipelineProvider());
    }

    public async Task DisposeAsync()
    {
        if (_transport != null)
            await _transport.DisposeAsync();

        _redis?.Dispose();

        if (_redisContainer != null)
            await _redisContainer.DisposeAsync();
    }

    private static bool IsDockerRunning()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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

    [Fact]
    public async Task SendAsync_ShouldAppendToRedisStream()
    {
        // Arrange
        var destination = "worker-queue";
        var streamKey = $"stream:{destination}";
        var testEvent = new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "stream-1",
            Data = "Stream payload",
            QoS = QualityOfService.AtLeastOnce
        };

        // Act
        await _transport!.SendAsync(testEvent, destination);
        await Task.Delay(200);

        // Assert - read back from Redis Stream
        var entries = await _redis!.GetDatabase().StreamReadAsync(streamKey, "0-0");
        entries.Should().NotBeNull();
        entries.Should().NotBeEmpty();
        var last = entries[^1];
        var dataField = last.Values.FirstOrDefault(v => v.Name == "data");
        dataField.Value.HasValue.Should().BeTrue("stream entry should contain 'data' field");

        // Deserialize
        var base64 = (string)dataField.Value!;
        var bytes = Convert.FromBase64String(base64);
        var restored = (TestEvent?)_serializer!.Deserialize(bytes, typeof(TestEvent));
        restored.Should().NotBeNull();
        restored!.Id.Should().Be("stream-1");
        restored.Data.Should().Be("Stream payload");
    }

    #region Test Models

    [MemoryPackable]
    private partial record TestEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        public QualityOfService QoS { get; init; } = QualityOfService.AtMostOnce;
    }

    [MemoryPackable]
    private partial record TestRequest : IRequest<TestResponse>
    {
        public required long MessageId { get; init; }
        public required string RequestId { get; init; }
        public required string Data { get; init; }
    }

    [MemoryPackable]
    private partial record TestResponse
    {
        public required string RequestId { get; init; }
        public required string Result { get; init; }
        public bool Success { get; init; }
    }

    #endregion
}




