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
/// NATS JetStream 特定功能测试
/// 测试 JetStream 的保留策略、消费者确认策略、消息重放等功能
/// Validates: Requirements 13.5-13.8, 18.6-18.10
/// </summary>
[Trait("Category", "Integration")]
[Trait("Backend", "NATS")]
[Trait("Requires", "Docker")]
public partial class NatsJetStreamFunctionalityTests : IAsyncLifetime
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
            ConnectTimeout = TimeSpan.FromSeconds(10)
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

    #region Stream Creation with Retention Policies

    /// <summary>
    /// 测试 WorkQueue 保留策略
    /// Validates: Requirements 13.5
    /// </summary>
    [Fact]
    public async Task NATS_JetStream_StreamCreation_WorkQueueRetentionPolicy()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_WORKQUEUE_{Guid.NewGuid():N}";
        var config = new StreamConfig(streamName, [$"{streamName}.>"])
        {
            Retention = StreamConfigRetention.Workqueue,
            MaxMsgs = 1000,
            MaxBytes = 1024 * 1024 // 1MB
        };

        // Act
        var stream = await _jetStream.CreateStreamAsync(config);

        // Assert
        stream.Should().NotBeNull();
        stream.Info.Config.Retention.Should().Be(StreamConfigRetention.Workqueue);
    }

    /// <summary>
    /// 测试 Interest 保留策略
    /// Validates: Requirements 13.5
    /// </summary>
    [Fact]
    public async Task NATS_JetStream_StreamCreation_InterestRetentionPolicy()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_INTEREST_{Guid.NewGuid():N}";
        var config = new StreamConfig(streamName, [$"{streamName}.>"])
        {
            Retention = StreamConfigRetention.Interest,
            MaxMsgs = 1000
        };

        // Act
        var stream = await _jetStream.CreateStreamAsync(config);

        // Assert
        stream.Should().NotBeNull();
        stream.Info.Config.Retention.Should().Be(StreamConfigRetention.Interest);
    }

    /// <summary>
    /// 测试 Limits 保留策略
    /// Validates: Requirements 13.5
    /// </summary>
    [Fact]
    public async Task NATS_JetStream_StreamCreation_LimitsRetentionPolicy()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_LIMITS_{Guid.NewGuid():N}";
        var config = new StreamConfig(streamName, [$"{streamName}.>"])
        {
            Retention = StreamConfigRetention.Limits,
            MaxMsgs = 100,
            MaxAge = TimeSpan.FromMinutes(5)
        };

        // Act
        var stream = await _jetStream.CreateStreamAsync(config);

        // Assert
        stream.Should().NotBeNull();
        stream.Info.Config.Retention.Should().Be(StreamConfigRetention.Limits);
        stream.Info.Config.MaxMsgs.Should().Be(100);
    }

    #endregion

    #region Consumer Acknowledgment Policies

    /// <summary>
    /// 测试 Explicit 确认策略
    /// Validates: Requirements 13.7
    /// </summary>
    [Fact]
    public async Task NATS_JetStream_Consumer_ExplicitAckPolicy()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_ACK_EXPLICIT_{Guid.NewGuid():N}";
        var stream = await _jetStream.CreateStreamAsync(new StreamConfig(streamName, [$"{streamName}.>"]));

        // Publish a test message
        await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "ack-test",
            Data = "test data"
        }));

        // Create consumer with Explicit ack policy
        var consumerConfig = new ConsumerConfig($"consumer_{Guid.NewGuid():N}")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromSeconds(30)
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Act
        var receivedCount = 0;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(2000).Token))
        {
            receivedCount++;
            await msg.AckAsync(); // Explicit acknowledgment
            break;
        }

        // Assert
        receivedCount.Should().Be(1);
    }

    /// <summary>
    /// 测试 None 确认策略
    /// Validates: Requirements 13.7
    /// </summary>
    [Fact]
    public async Task NATS_JetStream_Consumer_NoneAckPolicy()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_ACK_NONE_{Guid.NewGuid():N}";
        var stream = await _jetStream.CreateStreamAsync(new StreamConfig(streamName, [$"{streamName}.>"]));

        await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "none-ack",
            Data = "no ack needed"
        }));

        // Create consumer with None ack policy
        var consumerConfig = new ConsumerConfig($"consumer_{Guid.NewGuid():N}")
        {
            AckPolicy = ConsumerConfigAckPolicy.None
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Act
        var receivedCount = 0;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(2000).Token))
        {
            receivedCount++;
            // No acknowledgment needed
            break;
        }

        // Assert
        receivedCount.Should().Be(1);
    }

    /// <summary>
    /// 测试 All 确认策略
    /// Validates: Requirements 13.7
    /// </summary>
    [Fact]
    public async Task NATS_JetStream_Consumer_AllAckPolicy()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_ACK_ALL_{Guid.NewGuid():N}";
        var stream = await _jetStream.CreateStreamAsync(new StreamConfig(streamName, [$"{streamName}.>"]));

        // Publish multiple messages
        for (int i = 0; i < 5; i++)
        {
            await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = $"all-ack-{i}",
                Data = $"message {i}"
            }));
        }

        // Create consumer with All ack policy
        var consumerConfig = new ConsumerConfig($"consumer_{Guid.NewGuid():N}")
        {
            AckPolicy = ConsumerConfigAckPolicy.All
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Act
        var receivedCount = 0;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(3000).Token))
        {
            receivedCount++;
            if (receivedCount == 5)
            {
                await msg.AckAsync(); // Ack all previous messages
                break;
            }
        }

        // Assert
        receivedCount.Should().Be(5);
    }

    #endregion

    #region Message Replay

    /// <summary>
    /// 测试从特定序列号重放消息
    /// Validates: Requirements 13.8
    /// </summary>
    [Fact]
    public async Task NATS_JetStream_MessageReplay_FromSequence()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_REPLAY_{Guid.NewGuid():N}";
        var stream = await _jetStream.CreateStreamAsync(new StreamConfig(streamName, [$"{streamName}.>"]));

        // Publish 10 messages
        for (int i = 1; i <= 10; i++)
        {
            await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = $"replay-{i}",
                Data = $"message {i}"
            }));
        }

        await Task.Delay(500);

        // Create consumer starting from sequence 5
        var consumerConfig = new ConsumerConfig($"consumer_{Guid.NewGuid():N}")
        {
            DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartSequence,
            OptStartSeq = 5
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Act
        var receivedMessages = new List<int>();
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(3000).Token))
        {
            var evt = _serializer!.Deserialize<TestEvent>(msg.Data);
            var id = int.Parse(evt.Id.Split('-')[1]);
            receivedMessages.Add(id);
            await msg.AckAsync();
            
            if (receivedMessages.Count >= 6) break; // Should receive messages 5-10
        }

        // Assert
        receivedMessages.Should().HaveCountGreaterOrEqualTo(6);
        receivedMessages.First().Should().BeGreaterOrEqualTo(5);
    }

    /// <summary>
    /// 测试从特定时间点重放消息
    /// Validates: Requirements 13.8
    /// </summary>
    [Fact]
    public async Task NATS_JetStream_MessageReplay_FromTime()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_REPLAY_TIME_{Guid.NewGuid():N}";
        var stream = await _jetStream.CreateStreamAsync(new StreamConfig(streamName, [$"{streamName}.>"]));

        // Publish messages before timestamp
        for (int i = 1; i <= 3; i++)
        {
            await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = $"before-{i}",
                Data = $"before {i}"
            }));
        }

        await Task.Delay(1000);
        var replayTime = DateTime.UtcNow;
        await Task.Delay(1000);

        // Publish messages after timestamp
        for (int i = 1; i <= 3; i++)
        {
            await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = $"after-{i}",
                Data = $"after {i}"
            }));
        }

        // Create consumer starting from specific time
        var consumerConfig = new ConsumerConfig($"consumer_{Guid.NewGuid():N}")
        {
            DeliverPolicy = ConsumerConfigDeliverPolicy.ByStartTime,
            OptStartTime = replayTime
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Act
        var receivedIds = new List<string>();
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(3000).Token))
        {
            var evt = _serializer!.Deserialize<TestEvent>(msg.Data);
            receivedIds.Add(evt.Id);
            await msg.AckAsync();
            
            if (receivedIds.Count >= 3) break;
        }

        // Assert
        receivedIds.Should().HaveCountGreaterOrEqualTo(3);
        receivedIds.Should().AllSatisfy(id => id.Should().StartWith("after-"));
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
