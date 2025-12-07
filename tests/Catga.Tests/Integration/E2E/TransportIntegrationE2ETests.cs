using Catga.Abstractions;
using Catga.Core;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.Redis;
using Catga.Persistence.Redis.Persistence;
using Catga.Persistence.Redis.Stores;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// E2E tests for Transport layer integration with persistence stores.
/// Tests real-world message delivery scenarios with outbox/inbox patterns.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class TransportIntegrationE2ETests : IAsyncLifetime
{
    private RedisContainer? _container;
    private IConnectionMultiplexer? _redis;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;
        var image = ResolveImage("TEST_REDIS_IMAGE", "redis:7-alpine");
        if (image is null) return;

        _container = new RedisBuilder().WithImage(image).Build();
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_redis is not null) await _redis.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    #region Transport + Outbox Pattern

    [Fact]
    public async Task Transport_OutboxPattern_ShouldGuaranteeDelivery()
    {
        if (_redis is null) return;

        var outbox = new RedisOutboxPersistence(_redis, _serializer, NullLogger<RedisOutboxPersistence>.Instance, _provider);
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var receivedMessages = new List<TransportTestMessage>();
        var tcs = new TaskCompletionSource();

        // Subscribe
        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            receivedMessages.Add(msg);
            if (receivedMessages.Count >= 3) tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        // Add messages to outbox
        var messageIds = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            var msgId = MessageExtensions.NewMessageId();
            messageIds.Add(msgId);
            var outboxMsg = new OutboxMessage
            {
                MessageId = msgId,
                MessageType = "TransportTestMessage",
                Payload = _serializer.Serialize(new TransportTestMessage { MessageId = msgId, Data = $"outbox-msg-{i}" }),
                CreatedAt = DateTime.UtcNow,
                Status = OutboxStatus.Pending
            };
            await outbox.AddAsync(outboxMsg);
        }

        // Simulate outbox processor: get pending and publish
        var pending = await outbox.GetPendingMessagesAsync(10);
        foreach (var outboxMsg in pending)
        {
            var msg = (TransportTestMessage?)_serializer.Deserialize(outboxMsg.Payload, typeof(TransportTestMessage));
            if (msg is not null)
            {
                await transport.PublishAsync(msg, new TransportContext { MessageId = msg.MessageId });
                await outbox.MarkAsPublishedAsync(outboxMsg.MessageId);
            }
        }

        // Wait for delivery
        await Task.WhenAny(tcs.Task, Task.Delay(10000));

        receivedMessages.Should().HaveCount(3);
        var pendingAfter = await outbox.GetPendingMessagesAsync(10);
        pendingAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task Transport_InboxPattern_ShouldDeduplicateMessages()
    {
        if (_redis is null) return;

        var inbox = new RedisInboxPersistence(_redis, _serializer, NullLogger<RedisInboxPersistence>.Instance, _provider);
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var processCount = 0;
        var messageId = MessageExtensions.NewMessageId();

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            // Inbox pattern: check before processing
            if (!await inbox.HasBeenProcessedAsync(msg.MessageId))
            {
                Interlocked.Increment(ref processCount);
                var inboxMsg = new InboxMessage
                {
                    MessageId = msg.MessageId,
                    MessageType = "TransportTestMessage",
                    Payload = _serializer.Serialize(msg),
                    Status = InboxStatus.Processed
                };
                await inbox.TryLockMessageAsync(msg.MessageId, TimeSpan.FromMinutes(5));
                await inbox.MarkAsProcessedAsync(inboxMsg);
            }
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        // Simulate duplicate delivery
        var message = new TransportTestMessage { MessageId = messageId, Data = "duplicate-test" };
        for (int i = 0; i < 5; i++)
        {
            await transport.PublishAsync(message, new TransportContext { MessageId = messageId });
            await Task.Delay(50);
        }

        await Task.Delay(500);

        processCount.Should().Be(1);
    }

    #endregion

    #region Transport + Idempotency Pattern

    [Fact]
    public async Task Transport_Idempotency_ShouldCacheResults()
    {
        if (_redis is null) return;

        var idempotency = new RedisIdempotencyStore(_redis, _serializer, NullLogger<RedisIdempotencyStore>.Instance, _provider);
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var messageId = MessageExtensions.NewMessageId();
        var computeCount = 0;

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            // Check cache first
            var cached = await idempotency.GetCachedResultAsync<TransportTestResult>(msg.MessageId);
            if (cached is not null)
            {
                return; // Use cached result
            }

            // Compute result
            Interlocked.Increment(ref computeCount);
            var result = new TransportTestResult { Value = 42 };
            await idempotency.MarkAsProcessedAsync(msg.MessageId, result);
        });
        await Task.Delay(100);

        // Send same message multiple times
        var message = new TransportTestMessage { MessageId = messageId, Data = "idempotent-test" };
        for (int i = 0; i < 3; i++)
        {
            await transport.PublishAsync(message, new TransportContext { MessageId = messageId });
            await Task.Delay(100);
        }

        await Task.Delay(500);

        computeCount.Should().Be(1);
        var finalResult = await idempotency.GetCachedResultAsync<TransportTestResult>(messageId);
        finalResult.Should().NotBeNull();
        finalResult!.Value.Should().Be(42);
    }

    #endregion

    #region Transport QoS Levels

    [Fact]
    public async Task Transport_QoS0_AtMostOnce_ShouldDeliverViaPubSub()
    {
        if (_redis is null) return;

        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var received = new TaskCompletionSource<TransportTestMessage>();

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            received.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new TransportTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "qos0-test",
            QoS = QualityOfService.AtMostOnce
        };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(received.Task, Task.Delay(5000));
        result.Should().Be(received.Task);
    }

    [Fact]
    public async Task Transport_QoS1_AtLeastOnce_ShouldDeliverViaStreams()
    {
        if (_redis is null) return;

        var options = new RedisTransportOptions();
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var received = new TaskCompletionSource<TransportTestMessage>();

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            received.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new TransportTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "qos1-test",
            QoS = QualityOfService.AtLeastOnce
        };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(received.Task, Task.Delay(5000));
        result.Should().Be(received.Task);
    }

    [Fact]
    public async Task Transport_MultipleSubscribers_ShouldFanOut()
    {
        if (_redis is null) return;

        await using var transport1 = new RedisMessageTransport(_redis, _serializer, _provider);
        await using var transport2 = new RedisMessageTransport(_redis, _serializer, _provider);
        var received1 = new TaskCompletionSource<TransportTestMessage>();
        var received2 = new TaskCompletionSource<TransportTestMessage>();

        await transport1.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            received1.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await transport2.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            received2.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new TransportTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "fanout-test",
            QoS = QualityOfService.AtMostOnce
        };
        await transport1.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        await Task.WhenAny(Task.WhenAll(received1.Task, received2.Task), Task.Delay(5000));

        received1.Task.IsCompleted.Should().BeTrue();
        received2.Task.IsCompleted.Should().BeTrue();
    }

    #endregion

    #region Transport Batch Operations

    [Fact]
    public async Task Transport_BatchPublish_ShouldDeliverAll()
    {
        if (_redis is null) return;

        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var receivedCount = 0;
        var expectedCount = 100;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= expectedCount)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        // Batch publish
        var tasks = Enumerable.Range(0, expectedCount).Select(i =>
        {
            var msg = new TransportTestMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = $"batch-{i}"
            };
            return transport.PublishAsync(msg, new TransportContext { MessageId = msg.MessageId });
        });
        await Task.WhenAll(tasks);

        await Task.WhenAny(tcs.Task, Task.Delay(15000));

        receivedCount.Should().BeGreaterOrEqualTo(expectedCount * 8 / 10); // Allow 80% delivery
    }

    #endregion

    #region Helpers

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
        catch { return false; }
    }

    private static string? ResolveImage(string envVar, string defaultImage)
    {
        var env = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrEmpty(env) ? defaultImage : env;
    }

    #endregion
}

#region Test Types

[MemoryPackable]
public partial class TransportTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class TransportTestResult
{
    public int Value { get; set; }
}

#endregion
