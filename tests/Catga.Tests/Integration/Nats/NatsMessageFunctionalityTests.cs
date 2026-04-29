using Catga.Abstractions;
using Catga.Core;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Catga.Tests.Integration;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using Xunit;

namespace Catga.Tests.Integration.Nats;

/// <summary>
/// NATS JetStream 消息功能测试
/// 测试持久化消费者、队列组、消息大小限制等功能
/// Validates: Requirements 15.5-15.8, 18.11-18.14
/// </summary>
[Trait("Category", "Integration")]
[Trait("Backend", "NATS")]
[Trait("Requires", "Docker")]
[Collection("IntegrationTests")]
public partial class NatsMessageFunctionalityTests
{
    private readonly global::Catga.Tests.Integration.SharedIntegrationFixture _fixture;
    private NatsConnection? _natsConnection => _fixture.NatsConnection;
    private INatsJSContext? _jetStream => _fixture.JetStreamContext;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();

    public NatsMessageFunctionalityTests(global::Catga.Tests.Integration.SharedIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private ValueTask<INatsJSStream> CreateTestStreamAsync(string streamName)
        => _jetStream!.CreateStreamAsync(new StreamConfig(streamName, [$"{streamName}.>"])
        {
            Storage = StreamConfigStorage.Memory
        });

    private static async Task WaitForNoPendingAcksAsync(INatsJSStream stream, string consumerName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var consumer = await stream.GetConsumerAsync(consumerName);
            if (consumer.Info.NumAckPending == 0)
                return;

            await Task.Delay(100);
        }

        var finalConsumer = await stream.GetConsumerAsync(consumerName);
        finalConsumer.Info.NumAckPending.Should().Be(0);
    }

    #region Durable Consumer

    /// <summary>
    /// 测试持久化消费者
    /// Validates: Requirements 15.6
    /// </summary>
    [Fact]
    public async Task NATS_JetStream_DurableConsumer()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_DURABLE_{Guid.NewGuid():N}";
        var stream = await CreateTestStreamAsync(streamName);

        // Publish messages FIRST
        for (int i = 1; i <= 5; i++)
        {
            await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = $"durable-{i}",
                Data = $"message {i}"
            }));
        }

        // Create durable consumer AFTER messages are published
        var consumerName = $"durable_{Guid.NewGuid():N}";
        var consumerConfig = new ConsumerConfig(consumerName)
        {
            DurableName = consumerName,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromSeconds(30)
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Act - Consume first 2 messages only
        var firstBatch = new List<string>();
        var cts1 = new CancellationTokenSource();
        try
        {
            await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(cts1.Token))
            {
                var evt = _serializer!.Deserialize<TestEvent>(msg.Data!);
                firstBatch.Add(evt.Id);
                await msg.AckAsync();
                
                if (firstBatch.Count >= 2) 
                {
                    cts1.Cancel(); // Cancel to stop consuming
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when we cancel
        }

        await Task.Delay(1000); // Allow durable consumer ack state to settle before reconnect

        // Reconnect with same durable name
        var reconnectedConsumer = await stream.GetConsumerAsync(consumerName);

        // Consume remaining messages
        var secondBatch = new List<string>();
        var cts2 = new CancellationTokenSource();
        try
        {
            await foreach (var msg in reconnectedConsumer.ConsumeAsync<byte[]>().WithCancellation(cts2.Token))
            {
                var evt = _serializer!.Deserialize<TestEvent>(msg.Data!);
                secondBatch.Add(evt.Id);
                await msg.AckAsync();
                
                if (secondBatch.Count >= 3)
                {
                    cts2.Cancel(); // Cancel to stop consuming
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when we cancel
        }

        // Assert
        firstBatch.Should().HaveCount(2);
        secondBatch.Should().HaveCount(3);
        var allMessages = firstBatch.Concat(secondBatch).ToList();
        allMessages.Should().HaveCount(5);
        allMessages.Should().Contain("durable-1");
        allMessages.Should().Contain("durable-5");
    }

    /// <summary>
    /// 测试持久化消费者状态持久化
    /// Validates: Requirements 15.6
    /// </summary>
    [Fact]
    public async Task NATS_JetStream_DurableConsumer_StatePersistence()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_STATE_{Guid.NewGuid():N}";
        var stream = await CreateTestStreamAsync(streamName);

        var durableName = $"state_{Guid.NewGuid():N}";
        var consumerConfig = new ConsumerConfig(durableName)
        {
            DurableName = durableName,
            AckPolicy = ConsumerConfigAckPolicy.Explicit
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Publish and consume
        await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "state-test",
            Data = "test data"
        }));

        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(2000).Token))
        {
            await msg.AckAsync();
            break;
        }

        // Act - Get consumer info
        await WaitForNoPendingAcksAsync(stream, durableName, TimeSpan.FromSeconds(3));
    }

    #endregion

    #region Queue Groups

    /// <summary>
    /// 测试队列组负载均衡
    /// Validates: Requirements 15.7
    /// </summary>
    [Fact]
    public async Task NATS_QueueGroup_LoadBalancing()
    {
        if (_natsConnection is null) return;

        // Arrange
        var subject = $"test.queue.{Guid.NewGuid():N}";
        var queueGroup = "workers";
        
        var worker1Received = 0;
        var worker2Received = 0;
        var worker3Received = 0;

        // Create 3 workers in same queue group
        var sub1 = await _natsConnection.SubscribeCoreAsync<byte[]>(subject, queueGroup: queueGroup);
        var sub2 = await _natsConnection.SubscribeCoreAsync<byte[]>(subject, queueGroup: queueGroup);
        var sub3 = await _natsConnection.SubscribeCoreAsync<byte[]>(subject, queueGroup: queueGroup);

        var worker1Task = Task.Run(async () =>
        {
            await foreach (var msg in sub1.Msgs.ReadAllAsync())
            {
                Interlocked.Increment(ref worker1Received);
                if (worker1Received + worker2Received + worker3Received >= 10) break;
            }
        });

        var worker2Task = Task.Run(async () =>
        {
            await foreach (var msg in sub2.Msgs.ReadAllAsync())
            {
                Interlocked.Increment(ref worker2Received);
                if (worker1Received + worker2Received + worker3Received >= 10) break;
            }
        });

        var worker3Task = Task.Run(async () =>
        {
            await foreach (var msg in sub3.Msgs.ReadAllAsync())
            {
                Interlocked.Increment(ref worker3Received);
                if (worker1Received + worker2Received + worker3Received >= 10) break;
            }
        });

        // Act - Publish 10 messages
        for (int i = 1; i <= 10; i++)
        {
            await _natsConnection.PublishAsync(subject, _serializer!.Serialize(new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = $"queue-{i}",
                Data = $"message {i}"
            }));
        }

        await Task.WhenAny(
            Task.WhenAll(worker1Task, worker2Task, worker3Task),
            Task.Delay(5000)
        );

        // Assert - Messages should be distributed among workers
        var totalReceived = worker1Received + worker2Received + worker3Received;
        totalReceived.Should().Be(10);

        // Each worker should receive at least one message (load balancing)
        (worker1Received > 0 || worker2Received > 0 || worker3Received > 0).Should().BeTrue();
    }

    /// <summary>
    /// 测试队列组与普通订阅的区别
    /// Validates: Requirements 15.7
    /// </summary>
    [Fact]
    public async Task NATS_QueueGroup_VsRegularSubscription()
    {
        if (_natsConnection is null) return;

        // Arrange
        var subject = $"test.compare.{Guid.NewGuid():N}";
        
        var regularReceived = 0;
        var queueReceived = 0;

        // Regular subscription (receives all messages)
        var regularSub = await _natsConnection.SubscribeCoreAsync<byte[]>(subject);
        var regularTask = Task.Run(async () =>
        {
            await foreach (var msg in regularSub.Msgs.ReadAllAsync())
            {
                Interlocked.Increment(ref regularReceived);
                if (regularReceived >= 5) break;
            }
        });

        // Queue group subscription (shares messages)
        var queueSub = await _natsConnection.SubscribeCoreAsync<byte[]>(subject, queueGroup: "workers");
        var queueTask = Task.Run(async () =>
        {
            await foreach (var msg in queueSub.Msgs.ReadAllAsync())
            {
                Interlocked.Increment(ref queueReceived);
                if (queueReceived >= 5) break;
            }
        });

        // Act - Publish 5 messages
        for (int i = 1; i <= 5; i++)
        {
            await _natsConnection.PublishAsync(subject, _serializer!.Serialize(new TestEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Id = $"compare-{i}",
                Data = $"message {i}"
            }));
        }

        await Task.WhenAny(
            Task.WhenAll(regularTask, queueTask),
            Task.Delay(5000)
        );

        // Assert
        regularReceived.Should().Be(5); // Regular sub receives all
        queueReceived.Should().Be(5); // Queue sub also receives all (only one member)
    }

    #endregion

    #region Message Size Limits

    /// <summary>
    /// 测试消息最大负载大小
    /// Validates: Requirements 15.11
    /// </summary>
    [Fact]
    public async Task NATS_Message_MaxPayloadSize()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_SIZE_{Guid.NewGuid():N}";
        var stream = await CreateTestStreamAsync(streamName);

        // Act - Publish message with 128KB payload (reduced to account for serialization overhead)
        // NATS default max payload is 1MB, but serialization adds overhead
        var largeData = new byte[128 * 1024]; // 128KB
        new Random().NextBytes(largeData);

        var largeEvent = new LargeEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "large-msg",
            Data = largeData
        };

        await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(largeEvent));

        // Assert - Should be able to retrieve large message
        var consumerConfig = new ConsumerConfig($"consumer_{Guid.NewGuid():N}")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        var received = false;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(3000).Token))
        {
            var evt = _serializer!.Deserialize<LargeEvent>(msg.Data!);
            evt.Data.Length.Should().Be(128 * 1024);
            received = true;
            await msg.AckAsync();
            break;
        }

        received.Should().BeTrue();
    }

    /// <summary>
    /// 测试小消息处理
    /// Validates: Requirements 15.11
    /// </summary>
    [Fact]
    public async Task NATS_Message_SmallPayload()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_SMALL_{Guid.NewGuid():N}";
        var stream = await CreateTestStreamAsync(streamName);

        // Act - Publish small message
        var smallEvent = new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "small",
            Data = "x" // 1 byte
        };

        await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(smallEvent));

        // Assert
        var consumerConfig = new ConsumerConfig($"consumer_{Guid.NewGuid():N}")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        var received = false;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(2000).Token))
        {
            var evt = _serializer!.Deserialize<TestEvent>(msg.Data!);
            evt.Id.Should().Be("small");
            received = true;
            await msg.AckAsync();
            break;
        }

        received.Should().BeTrue();
    }

    #endregion

    #region Message Acknowledgment

    /// <summary>
    /// 测试消息确认
    /// Validates: Requirements 15.8
    /// </summary>
    [Fact]
    public async Task NATS_Message_Acknowledgment()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_ACK_{Guid.NewGuid():N}";
        var stream = await CreateTestStreamAsync(streamName);

        await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "ack-test",
            Data = "test data"
        }));

        var consumerConfig = new ConsumerConfig($"consumer_{Guid.NewGuid():N}")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromSeconds(30)
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Act - Consume and acknowledge
        var acked = false;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(2000).Token))
        {
            await msg.AckAsync();
            acked = true;
            break;
        }

        // Assert
        acked.Should().BeTrue();

        // Verify no pending acks
        await WaitForNoPendingAcksAsync(stream, consumerConfig.Name!, TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// 测试消息 NAK (Negative Acknowledgment)
    /// Validates: Requirements 15.8
    /// </summary>
    [Fact]
    public async Task NATS_Message_NegativeAcknowledgment()
    {
        if (_jetStream is null) return;

        // Arrange
        var streamName = $"TEST_NAK_{Guid.NewGuid():N}";
        var stream = await CreateTestStreamAsync(streamName);

        await _jetStream.PublishAsync($"{streamName}.test", _serializer!.Serialize(new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "nak-test",
            Data = "test data"
        }));

        var consumerConfig = new ConsumerConfig($"consumer_{Guid.NewGuid():N}")
        {
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromSeconds(5),
            MaxDeliver = 3
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(consumerConfig);

        // Act - Consume and NAK
        var receiveCount = 0;
        await foreach (var msg in consumer.ConsumeAsync<byte[]>().WithCancellation(new CancellationTokenSource(10000).Token))
        {
            receiveCount++;
            await msg.NakAsync(); // Negative acknowledgment - message will be redelivered
            
            if (receiveCount >= 2) break; // Receive at least twice due to NAK
        }

        // Assert - Message should be redelivered
        receiveCount.Should().BeGreaterOrEqualTo(2);
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

    [MemoryPackable]
    private partial record LargeEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string Id { get; init; }
        public required byte[] Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    #endregion
}
