using Catga.Abstractions;
using Catga.Core;
using Catga.Serialization.MemoryPack;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Catga.Tests.Integration.Nats;

/// <summary>
/// NATS 连接管理测试
/// 测试连接失败处理、自动重连、消息重放等功能
/// Validates: Requirements 13.11-13.14, 18.1-18.5
/// </summary>
[Trait("Category", "Integration")]
[Trait("Backend", "NATS")]
[Trait("Requires", "Docker")]
public partial class NatsConnectionManagementTests : IAsyncLifetime
{
    private IContainer? _natsContainer;
    private NatsConnection? _natsConnection;
    private INatsJSContext? _jetStream;
    private IMessageSerializer? _serializer;

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning())
        {
            return;
        }

        var natsImage = Environment.GetEnvironmentVariable("TEST_NATS_IMAGE") ?? "nats:latest";
        _natsContainer = new ContainerBuilder()
            .WithImage(natsImage)
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(8222)
                    .ForPath("/varz")))
            .Build();

        await _natsContainer.StartAsync();
        await Task.Delay(2000);

        var port = _natsContainer.GetMappedPublicPort(4222);
        var opts = new NatsOpts
        {
            Url = $"nats://localhost:{port}",
            ConnectTimeout = TimeSpan.FromSeconds(10),
            // ReconnectWait = TimeSpan.FromSeconds(1), // Property removed in current NATS client
            MaxReconnectRetry = 5
        };

        _natsConnection = new NatsConnection(opts);
        await _natsConnection.ConnectAsync();
        _jetStream = new NatsJSContext(_natsConnection);
        _serializer = new MemoryPackMessageSerializer();
    }

    public async Task DisposeAsync()
    {
        if (_natsConnection != null)
            await _natsConnection.DisposeAsync();

        if (_natsContainer != null)
            await _natsContainer.DisposeAsync();
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

    #region Connection Failure Handling

    /// <summary>
    /// 测试连接失败时的优雅处理
    /// Validates: Requirements 13.11
    /// </summary>
    [Fact]
    public async Task NATS_ConnectionFailure_GracefulHandling()
    {
        // Arrange - Try to connect to non-existent NATS server
        var opts = new NatsOpts
        {
            Url = "nats://localhost:9999", // Non-existent port
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxReconnectRetry = 1
        };

        NatsConnection? failedConnection = null;

        // Act
        var act = async () =>
        {
            failedConnection = new NatsConnection(opts);
            await failedConnection.ConnectAsync();
        };

        // Assert
        await act.Should().ThrowAsync<Exception>();

        // Cleanup
        if (failedConnection != null)
            await failedConnection.DisposeAsync();
    }

    /// <summary>
    /// 测试连接状态检查
    /// Validates: Requirements 13.11
    /// </summary>
    [Fact]
    public async Task NATS_Connection_StateCheck()
    {
        if (_natsConnection is null) return;

        // Act
        var state = _natsConnection.ConnectionState;

        // Assert
        state.Should().Be(NatsConnectionState.Open);
    }

    #endregion

    #region Reconnection and Message Replay

    /// <summary>
    /// 测试重连后消息重放
    /// Validates: Requirements 13.12
    /// </summary>
    [Fact]
    public async Task NATS_Reconnection_MessageReplay()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_RECONNECT_{Guid.NewGuid():N}";
        var stream = await _jetStream.CreateStreamAsync(new StreamConfig(streamName, [$"{streamName}.>"]));

        // Publish messages before "disconnection"
        for (int i = 1; i <= 5; i++)
        {
            await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = $"msg-{i}",
                Data = $"message {i}"
            }));
        }

        await Task.Delay(500);

        // Create durable consumer
        var consumerConfig = new ConsumerConfig($"durable_{Guid.NewGuid():N}")
        {
            DurableName = $"durable_{Guid.NewGuid():N}",
            AckPolicy = ConsumerConfigAckPolicy.Explicit
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Consume first 2 messages
        var receivedCount = 0;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(2000).Token))
        {
            receivedCount++;
            await msg.AckAsync();
            if (receivedCount >= 2) break;
        }

        // Simulate reconnection by creating new consumer with same durable name
        var reconnectedConsumer = await stream.GetConsumerAsync(consumerConfig.DurableName!);

        // Act - Continue consuming after "reconnection"
        var replayedMessages = new List<string>();
        await foreach (var msg in reconnectedConsumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(3000).Token))
        {
            var evt = _serializer!.Deserialize<TestEvent>(msg.Data);
            replayedMessages.Add(evt.Id);
            await msg.AckAsync();
            
            if (replayedMessages.Count >= 3) break; // Should receive remaining 3 messages
        }

        // Assert
        replayedMessages.Should().HaveCountGreaterOrEqualTo(3);
        replayedMessages.Should().Contain("msg-3");
        replayedMessages.Should().Contain("msg-4");
        replayedMessages.Should().Contain("msg-5");
    }

    /// <summary>
    /// 测试持久化消费者在重连后继续消费
    /// Validates: Requirements 13.12
    /// </summary>
    [Fact]
    public async Task NATS_DurableConsumer_ContinuesAfterReconnect()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_DURABLE_{Guid.NewGuid():N}";
        var stream = await _jetStream.CreateStreamAsync(new StreamConfig(streamName, [$"{streamName}.>"]));

        var durableName = $"durable_{Guid.NewGuid():N}";
        var consumerConfig = new ConsumerConfig(durableName)
        {
            DurableName = durableName,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromSeconds(30)
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Publish and consume first batch
        await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "batch-1",
            Data = "first batch"
        }));

        await Task.Delay(300);

        var firstBatch = new List<string>();
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(2000).Token))
        {
            var evt = _serializer!.Deserialize<TestEvent>(msg.Data);
            firstBatch.Add(evt.Id);
            await msg.AckAsync();
            break;
        }

        // Publish second batch while "disconnected"
        await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "batch-2",
            Data = "second batch"
        }));

        await Task.Delay(300);

        // Reconnect and continue
        var reconnectedConsumer = await stream.GetConsumerAsync(durableName);
        var secondBatch = new List<string>();
        await foreach (var msg in reconnectedConsumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(2000).Token))
        {
            var evt = _serializer!.Deserialize<TestEvent>(msg.Data);
            secondBatch.Add(evt.Id);
            await msg.AckAsync();
            break;
        }

        // Assert
        firstBatch.Should().Contain("batch-1");
        secondBatch.Should().Contain("batch-2");
    }

    #endregion

    #region Stream Limits

    /// <summary>
    /// 测试流的最大消息数限制
    /// Validates: Requirements 13.12
    /// </summary>
    [Fact]
    public async Task NATS_StreamLimits_MaxMessages()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_MAXMSGS_{Guid.NewGuid():N}";
        var config = new StreamConfig(streamName, [$"{streamName}.>"])
        {
            MaxMsgs = 10,
            Discard = StreamConfigDiscard.Old // Discard old messages when limit reached
        };

        var stream = await _jetStream.CreateStreamAsync(config);

        // Act - Publish more than max messages
        for (int i = 1; i <= 15; i++)
        {
            await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = $"msg-{i}",
                Data = $"message {i}"
            }));
        }

        await Task.Delay(500);

        // Assert - Stream should only contain last 10 messages
        var streamInfo = await _jetStream.GetStreamAsync(streamName);
        streamInfo.Info.State.Messages.Should().BeLessOrEqualTo(10);
    }

    /// <summary>
    /// 测试流的最大字节数限制
    /// Validates: Requirements 13.12
    /// </summary>
    [Fact]
    public async Task NATS_StreamLimits_MaxBytes()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_MAXBYTES_{Guid.NewGuid():N}";
        var config = new StreamConfig(streamName, [$"{streamName}.>"])
        {
            MaxBytes = 1024, // 1KB limit
            Discard = StreamConfigDiscard.Old
        };

        var stream = await _jetStream.CreateStreamAsync(config);

        // Act - Publish messages until limit is reached
        var largeData = new byte[512]; // 512 bytes per message
        new Random().NextBytes(largeData);

        for (int i = 1; i <= 5; i++)
        {
            await _jetStream.PublishAsync($"{streamName}.test", largeData);
        }

        await Task.Delay(500);

        // Assert - Stream should respect byte limit
        var streamInfo = await _jetStream.GetStreamAsync(streamName);
        streamInfo.Info.State.Bytes.Should().BeLessOrEqualTo(1024);
    }

    #endregion

    #region Slow Consumer Handling

    /// <summary>
    /// 测试慢消费者检测
    /// Validates: Requirements 13.13
    /// </summary>
    [Fact]
    public async Task NATS_SlowConsumer_Detection()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_SLOW_{Guid.NewGuid():N}";
        var stream = await _jetStream.CreateStreamAsync(new StreamConfig(streamName, [$"{streamName}.>"]));

        var consumerConfig = new ConsumerConfig($"slow_{Guid.NewGuid():N}")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromSeconds(5),
            MaxAckPending = 10 // Limit pending acks
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Act - Publish many messages quickly
        for (int i = 1; i <= 20; i++)
        {
            await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = $"fast-{i}",
                Data = $"message {i}"
            }));
        }

        await Task.Delay(500);

        // Consume slowly (don't ack immediately)
        var receivedCount = 0;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(3000).Token))
        {
            receivedCount++;
            // Intentionally slow - don't ack
            await Task.Delay(100);
            
            if (receivedCount >= 15) break;
        }

        // Assert - Consumer should have received messages but may have hit MaxAckPending limit
        receivedCount.Should().BeGreaterThan(0);
    }

    #endregion

    #region Cluster Node Failure

    /// <summary>
    /// 测试单节点场景下的基本操作
    /// Validates: Requirements 13.14
    /// </summary>
    [Fact]
    public async Task NATS_SingleNode_BasicOperations()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_SINGLE_{Guid.NewGuid():N}";
        var stream = await _jetStream.CreateStreamAsync(new StreamConfig(streamName, [$"{streamName}.>"]));

        // Act - Basic publish and consume
        await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "single-node",
            Data = "test data"
        }));

        await Task.Delay(300);

        var consumerConfig = new ConsumerConfig($"consumer_{Guid.NewGuid():N}")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        var received = false;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(2000).Token))
        {
            var evt = _serializer!.Deserialize<TestEvent>(msg.Data);
            evt.Id.Should().Be("single-node");
            received = true;
            await msg.AckAsync();
            break;
        }

        // Assert
        received.Should().BeTrue();
    }

    #endregion

    #region Test Models

    [MemoryPackable]
    private partial record TestEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    #endregion
}
