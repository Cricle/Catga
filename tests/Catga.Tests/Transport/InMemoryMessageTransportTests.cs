using Catga.Core;
using Catga.Messages;
using Catga.Transport;
using FluentAssertions;
using MemoryPack;

namespace Catga.Tests.Transport;

/// <summary>
/// InMemory message transport tests - QoS support, idempotency, retry
/// Target: 90%+ coverage
/// </summary>
public class InMemoryMessageTransportTests
{
    private readonly InMemoryMessageTransport _transport = new();

    #region Basic Publish/Subscribe Tests (4 tests)

    [Fact]
    public async Task PublishAsync_WithSubscriber_ShouldDeliverMessage()
    {
        // Arrange
        var received = false;
        var receivedMessage = default(TestTransportMessage);
        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            received = true;
            receivedMessage = msg;
            return Task.CompletedTask;
        });

        var message = new TestTransportMessage(123, "Test");

        // Act
        await _transport.PublishAsync(message);
        await Task.Delay(50); // Allow async processing

        // Assert
        received.Should().BeTrue();
        receivedMessage.Should().NotBeNull();
        receivedMessage!.Id.Should().Be(123);
        receivedMessage.Name.Should().Be("Test");
    }

    [Fact]
    public async Task PublishAsync_NoSubscribers_ShouldNotThrow()
    {
        // Arrange
        var message = new TestTransportMessage(456, "NoSubscriber");

        // Act & Assert
        var act = async () => await _transport.PublishAsync(message);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_ShouldDeliverToAll()
    {
        // Arrange
        var received1 = false;
        var received2 = false;
        var received3 = false;

        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            received1 = true;
            return Task.CompletedTask;
        });
        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            received2 = true;
            return Task.CompletedTask;
        });
        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            received3 = true;
            return Task.CompletedTask;
        });

        var message = new TestTransportMessage(789, "MultiSubscriber");

        // Act
        await _transport.PublishAsync(message);
        await Task.Delay(50);

        // Assert
        received1.Should().BeTrue();
        received2.Should().BeTrue();
        received3.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_ShouldBehaveLikePublishAsync()
    {
        // Arrange
        var received = false;
        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            received = true;
            return Task.CompletedTask;
        });

        var message = new TestTransportMessage(111, "Send");

        // Act
        await _transport.SendAsync(message, "destination");
        await Task.Delay(50);

        // Assert
        received.Should().BeTrue();
    }

    #endregion

    #region QoS Tests (5 tests)

    [Fact]
    public async Task PublishAsync_QoS0_AtMostOnce_ShouldFireAndForget()
    {
        // Arrange
        var receivedCount = 0;
        await _transport.SubscribeAsync<QoS0Message>((msg, ctx) =>
        {
            receivedCount++;
            throw new Exception("Handler error"); // Should be swallowed
        });

        var message = new QoS0Message(222, "QoS0");

        // Act
        await _transport.PublishAsync(message);
        await Task.Delay(100);

        // Assert - message delivered but error swallowed
        receivedCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_QoS1_AtLeastOnce_WaitForResult_ShouldWaitForCompletion()
    {
        // Arrange
        var handlerCompleted = false;
        await _transport.SubscribeAsync<QoS1WaitMessage>((msg, ctx) =>
        {
            handlerCompleted = true;
            return Task.CompletedTask;
        });

        var message = new QoS1WaitMessage(333, "QoS1Wait");

        // Act
        await _transport.PublishAsync(message);

        // Assert - should wait for handler completion
        handlerCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_QoS1_AtLeastOnce_AsyncRetry_ShouldRetryOnFailure()
    {
        // Arrange
        var attemptCount = 0;
        await _transport.SubscribeAsync<QoS1RetryMessage>((msg, ctx) =>
        {
            attemptCount++;
            if (attemptCount < 3)
                throw new Exception("Transient error");
            return Task.CompletedTask;
        });

        var message = new QoS1RetryMessage(444, "QoS1Retry");

        // Act
        await _transport.PublishAsync(message);
        await Task.Delay(1000); // Wait for retries (100ms, 200ms, 400ms)

        // Assert - should retry up to 3 times
        attemptCount.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task PublishAsync_QoS2_ExactlyOnce_ShouldPreventDuplicates()
    {
        // Arrange
        var receivedCount = 0;
        await _transport.SubscribeAsync<QoS2Message>((msg, ctx) =>
        {
            receivedCount++;
            return Task.CompletedTask;
        });

        var message = new QoS2Message(555, "QoS2");
        var context = new TransportContext { MessageId = MessageExtensions.NewMessageId()-123L };

        // Act - publish same message twice with same MessageId
        await _transport.PublishAsync(message, context);
        await _transport.PublishAsync(message, context);
        await Task.Delay(50);

        // Assert - should only process once
        receivedCount.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_QoS2_DifferentMessageIds_ShouldProcessBoth()
    {
        // Arrange
        var receivedCount = 0;
        await _transport.SubscribeAsync<QoS2Message>((msg, ctx) =>
        {
            receivedCount++;
            return Task.CompletedTask;
        });

        var message1 = new QoS2Message(666, "QoS2-1");
        var message2 = new QoS2Message(777, "QoS2-2");
        var context1 = new TransportContext { MessageId = MessageExtensions.NewMessageId() };
        var context2 = new TransportContext { MessageId = MessageExtensions.NewMessageId() };

        // Act
        await _transport.PublishAsync(message1, context1);
        await _transport.PublishAsync(message2, context2);
        await Task.Delay(50);

        // Assert - should process both
        receivedCount.Should().Be(2);
    }

    #endregion

    #region Batch Operations Tests (2 tests)

    [Fact]
    public async Task PublishBatchAsync_MultipleMessages_ShouldDeliverAll()
    {
        // Arrange
        var receivedIds = new List<int>();
        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            lock (receivedIds)
            {
                receivedIds.Add(msg.Id);
            }
            return Task.CompletedTask;
        });

        var messages = new[]
        {
            new TestTransportMessage(1, "Batch1"),
            new TestTransportMessage(2, "Batch2"),
            new TestTransportMessage(3, "Batch3")
        };

        // Act
        await _transport.PublishBatchAsync(messages);
        await Task.Delay(100);

        // Assert
        receivedIds.Should().HaveCount(3);
        receivedIds.Should().Contain(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task SendBatchAsync_ShouldBehaveLikePublishBatchAsync()
    {
        // Arrange
        var receivedCount = 0;
        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        var messages = new[]
        {
            new TestTransportMessage(10, "SendBatch1"),
            new TestTransportMessage(20, "SendBatch2")
        };

        // Act
        await _transport.SendBatchAsync(messages, "destination");
        await Task.Delay(100);

        // Assert
        receivedCount.Should().Be(2);
    }

    #endregion

    #region TransportContext Tests (3 tests)

    [Fact]
    public async Task PublishAsync_WithContext_ShouldPassContextToHandler()
    {
        // Arrange
        TransportContext? receivedContext = null;
        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            receivedContext = ctx;
            return Task.CompletedTask;
        });

        var message = new TestTransportMessage(888, "Context");
        var context = new TransportContext
        {
            MessageId = 1234567890L,
            CorrelationId = 9876543210L,
            MessageType = "CustomType"
        };

        // Act
        await _transport.PublishAsync(message, context);
        await Task.Delay(50);

        // Assert
        receivedContext.Should().NotBeNull();
        receivedContext!.Value.MessageId.Should().Be(1234567890L);
        receivedContext.Value.CorrelationId.Should().Be(9876543210L);
    }

    [Fact]
    public async Task PublishAsync_NoContext_ShouldGenerateDefaultContext()
    {
        // Arrange
        TransportContext? receivedContext = null;
        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            receivedContext = ctx;
            return Task.CompletedTask;
        });

        var message = new TestTransportMessage(999, "NoContext");

        // Act
        await _transport.PublishAsync(message);
        await Task.Delay(50);

        // Assert
        receivedContext.Should().NotBeNull();
        receivedContext!.Value.MessageId.Should().BeGreaterThan(0);
        receivedContext.Value.MessageType.Should().NotBeNullOrEmpty();
        receivedContext.Value.SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishAsync_WithSentAt_ShouldPreserveSentAt()
    {
        // Arrange
        TransportContext? receivedContext = null;
        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            receivedContext = ctx;
            return Task.CompletedTask;
        });

        var message = new TestTransportMessage(1000, "SentAt");
        var sentAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var context = new TransportContext { SentAt = sentAt };

        // Act
        await _transport.PublishAsync(message, context);
        await Task.Delay(50);

        // Assert
        receivedContext.Should().NotBeNull();
        receivedContext!.Value.SentAt.Should().Be(sentAt);
    }

    #endregion

    #region Concurrent Tests (2 tests)

    [Fact]
    public async Task PublishAsync_Concurrent_ShouldHandleAllMessages()
    {
        // Arrange
        var receivedCount = 0;
        await _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        // Act - publish 100 messages concurrently
        var tasks = Enumerable.Range(1, 100)
            .Select(i => _transport.PublishAsync(new TestTransportMessage(i, $"Concurrent{i}")))
            .ToArray();
        await Task.WhenAll(tasks);
        await Task.Delay(200);

        // Assert
        receivedCount.Should().Be(100);
    }

    [Fact]
    public async Task SubscribeAsync_Concurrent_ShouldBeThreadSafe()
    {
        // Arrange & Act - subscribe 10 handlers concurrently
        var tasks = Enumerable.Range(1, 10)
            .Select(i => _transport.SubscribeAsync<TestTransportMessage>((msg, ctx) => Task.CompletedTask))
            .ToArray();

        // Assert - should not throw
        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Property Tests (3 tests)

    [Fact]
    public void Name_ShouldReturnInMemory()
    {
        // Act & Assert
        _transport.Name.Should().Be("InMemory");
    }

    [Fact]
    public void BatchOptions_ShouldBeNull()
    {
        // Act & Assert
        _transport.BatchOptions.Should().BeNull();
    }

    [Fact]
    public void CompressionOptions_ShouldBeNull()
    {
        // Act & Assert
        _transport.CompressionOptions.Should().BeNull();
    }

    #endregion
}

// Test message types
[MemoryPackable]
public partial record TestTransportMessage(int Id, string Name) : IMessage
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public QualityOfService QoS => QualityOfService.AtLeastOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

[MemoryPackable]
public partial record QoS0Message(int Id, string Name) : IMessage
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public QualityOfService QoS => QualityOfService.AtMostOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

[MemoryPackable]
public partial record QoS1WaitMessage(int Id, string Name) : IMessage
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public QualityOfService QoS => QualityOfService.AtLeastOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

[MemoryPackable]
public partial record QoS1RetryMessage(int Id, string Name) : IMessage
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public QualityOfService QoS => QualityOfService.AtLeastOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.AsyncRetry;
}

[MemoryPackable]
public partial record QoS2Message(int Id, string Name) : IMessage
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
    public QualityOfService QoS => QualityOfService.ExactlyOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

