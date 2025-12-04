using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catga.Tests.Transport;

/// <summary>
/// Extended tests for Transport components to improve coverage.
/// </summary>
public class TransportExtendedTests
{
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    #region InMemoryMessageTransport Extended Tests

    [Fact]
    public async Task InMemoryTransport_PublishAsync_NoSubscribers_ShouldNotThrow()
    {
        var transport = new InMemoryMessageTransport(null, _provider);
        var message = new TransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "no-sub" };

        await transport.PublishAsync(message);

        // Should not throw
    }

    [Fact]
    public async Task InMemoryTransport_PublishAsync_QoS0_ShouldDeliver()
    {
        var transport = new InMemoryMessageTransport(null, _provider);
        var tcs = new TaskCompletionSource<TransportTestMessage>();

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });

        var message = new TransportTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "qos0",
            QoS = QualityOfService.AtMostOnce
        };
        await transport.PublishAsync(message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task InMemoryTransport_PublishAsync_QoS1_ShouldDeliver()
    {
        var transport = new InMemoryMessageTransport(null, _provider);
        var tcs = new TaskCompletionSource<TransportTestMessage>();

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });

        var message = new TransportTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "qos1",
            QoS = QualityOfService.AtLeastOnce
        };
        await transport.PublishAsync(message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task InMemoryTransport_PublishAsync_QoS2_ShouldDeduplicate()
    {
        var transport = new InMemoryMessageTransport(null, _provider);
        var received = 0;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            Interlocked.Increment(ref received);
            tcs.TrySetResult();
            await Task.CompletedTask;
        });

        var messageId = MessageExtensions.NewMessageId();
        var message = new TransportTestMessage
        {
            MessageId = messageId,
            Data = "qos2",
            QoS = QualityOfService.ExactlyOnce
        };
        var ctx = new TransportContext { MessageId = messageId };

        // Publish twice
        await transport.PublishAsync(message, ctx);
        await transport.PublishAsync(message, ctx);

        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        await Task.Delay(500);

        received.Should().Be(1);
    }

    [Fact]
    public async Task InMemoryTransport_PublishBatchAsync_ShouldDeliverAll()
    {
        var transport = new InMemoryMessageTransport(null, _provider);
        var receivedCount = 0;
        var tcs = new TaskCompletionSource();
        const int batchSize = 5;

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= batchSize)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        var messages = Enumerable.Range(0, batchSize)
            .Select(i => new TransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"batch-{i}" })
            .ToList();

        await transport.PublishBatchAsync(messages);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task InMemoryTransport_SendAsync_ShouldDeliver()
    {
        var transport = new InMemoryMessageTransport(null, _provider);
        var tcs = new TaskCompletionSource<TransportTestMessage>();

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });

        var message = new TransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "send" };
        await transport.SendAsync(message, "test-dest");

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public void InMemoryTransport_Name_ShouldBeInMemory()
    {
        var transport = new InMemoryMessageTransport(null, _provider);

        transport.Name.Should().Be("InMemory");
    }

    [Fact]
    public void InMemoryTransport_BatchOptions_ShouldBeNull()
    {
        var transport = new InMemoryMessageTransport(null, _provider);

        transport.BatchOptions.Should().BeNull();
    }

    [Fact]
    public void InMemoryTransport_CompressionOptions_ShouldBeNull()
    {
        var transport = new InMemoryMessageTransport(null, _provider);

        transport.CompressionOptions.Should().BeNull();
    }

    #endregion

    #region TransportContext Extended Tests

    [Fact]
    public void TransportContext_DefaultValues_ShouldBeSet()
    {
        var ctx = new TransportContext();

        ctx.MessageId.Should().BeNull();
        ctx.MessageType.Should().BeNull();
    }

    [Fact]
    public void TransportContext_WithAllProperties_ShouldBeAccessible()
    {
        var now = DateTime.UtcNow;
        var ctx = new TransportContext
        {
            MessageId = 123,
            MessageType = "TestType",
            CorrelationId = 456,
            SentAt = now
        };

        ctx.MessageId.Should().Be(123);
        ctx.MessageType.Should().Be("TestType");
        ctx.CorrelationId.Should().Be(456);
        ctx.SentAt.Should().Be(now);
    }

    #endregion

    #region TypedSubscribers Tests

    [Fact]
    public async Task TypedSubscribers_MultipleSubscribers_ShouldDeliverToAll()
    {
        var transport = new InMemoryMessageTransport(null, _provider);
        var received1 = false;
        var received2 = false;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            received1 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await transport.SubscribeAsync<TransportTestMessage>(async (msg, ctx) =>
        {
            received2 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });

        var message = new TransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "multi" };
        await transport.PublishAsync(message);

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        (received1 && received2).Should().BeTrue();
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

#endregion
