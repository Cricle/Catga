using Catga.Abstractions;
using Catga.Core;
using Catga.Resilience;
using Catga.Tests.PropertyTests.Generators;
using Catga.Transport;
using FsCheck;
using FsCheck.Xunit;
using FluentAssertions;
using MemoryPack;
using System.Collections.Concurrent;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// InMemoryMessageTransport 属性测试
/// 使用 FsCheck 进行属性测试验证
/// 
/// 注意: FsCheck.Xunit 的 [Property] 特性要求测试类有无参构造函数
/// </summary>
[Trait("Category", "Property")]
[Trait("Backend", "InMemory")]
public class InMemoryTransportPropertyTests
{
    /// <summary>
    /// Property 8: Transport Delivery Guarantee
    /// 
    /// *For any* published message and set of active subscribers, all subscribers SHALL receive the message.
    /// 
    /// **Validates: Requirements 4.17**
    /// 
    /// Feature: tdd-validation, Property 8: Transport Delivery Guarantee
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property Transport_Publish_AllSubscribersReceive()
    {
        return Prop.ForAll(
            Gen.Choose(1, 5).ToArbitrary(), // Number of subscribers (1-5)
            (subscriberCount) =>
            {
                // Arrange - Create a fresh transport for each test iteration
                var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
                var receivedMessages = new ConcurrentBag<TransportPropertyTestMessage>();
                var subscriptionTasks = new List<Task>();

                // Subscribe multiple handlers
                for (int i = 0; i < subscriberCount; i++)
                {
                    var task = transport.SubscribeAsync<TransportPropertyTestMessage>((msg, ctx) =>
                    {
                        receivedMessages.Add(msg);
                        return Task.CompletedTask;
                    });
                    subscriptionTasks.Add(task);
                }

                // Wait for all subscriptions to complete
                Task.WhenAll(subscriptionTasks).GetAwaiter().GetResult();

                // Create a unique message for this test iteration
                var messageId = MessageExtensions.NewMessageId();
                var message = new TransportPropertyTestMessage
                {
                    MessageId = messageId,
                    Content = $"Test-{messageId}",
                    Timestamp = DateTime.UtcNow
                };

                // Act - Publish the message
                transport.PublishAsync(message).GetAwaiter().GetResult();

                // Allow time for async delivery
                Task.Delay(100).GetAwaiter().GetResult();

                // Assert - All subscribers should receive the message
                var receivedCount = receivedMessages.Count;
                var allReceivedCorrectMessage = receivedMessages.All(m => m.MessageId == messageId);

                return receivedCount == subscriberCount && allReceivedCorrectMessage;
            });
    }

    /// <summary>
    /// Property 8 (Alternative): Transport Delivery Guarantee with Multiple Messages
    /// 
    /// *For any* sequence of published messages and set of active subscribers, 
    /// all subscribers SHALL receive all messages.
    /// 
    /// **Validates: Requirements 4.17**
    /// 
    /// Feature: tdd-validation, Property 8: Transport Delivery Guarantee (Multiple Messages)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.QuickMaxTest)]
    public Property Transport_PublishMultiple_AllSubscribersReceiveAll()
    {
        return Prop.ForAll(
            Gen.Choose(1, 3).ToArbitrary(), // Number of subscribers (1-3)
            Gen.Choose(1, 5).ToArbitrary(), // Number of messages (1-5)
            (subscriberCount, messageCount) =>
            {
                // Arrange - Create a fresh transport for each test iteration
                var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
                var receivedMessages = new ConcurrentBag<TransportPropertyTestMessage>();
                var subscriptionTasks = new List<Task>();

                // Subscribe multiple handlers
                for (int i = 0; i < subscriberCount; i++)
                {
                    var task = transport.SubscribeAsync<TransportPropertyTestMessage>((msg, ctx) =>
                    {
                        receivedMessages.Add(msg);
                        return Task.CompletedTask;
                    });
                    subscriptionTasks.Add(task);
                }

                // Wait for all subscriptions to complete
                Task.WhenAll(subscriptionTasks).GetAwaiter().GetResult();

                // Create unique messages for this test iteration
                var messages = new List<TransportPropertyTestMessage>();
                for (int i = 0; i < messageCount; i++)
                {
                    var msgId = MessageExtensions.NewMessageId();
                    messages.Add(new TransportPropertyTestMessage
                    {
                        MessageId = msgId,
                        Content = $"Test-{msgId}",
                        Timestamp = DateTime.UtcNow
                    });
                }

                // Act - Publish all messages
                foreach (var msg in messages)
                {
                    transport.PublishAsync(msg).GetAwaiter().GetResult();
                }

                // Allow time for async delivery
                Task.Delay(150).GetAwaiter().GetResult();

                // Assert - Total received should be subscriberCount * messageCount
                var expectedTotal = subscriberCount * messageCount;
                var receivedCount = receivedMessages.Count;

                // Each message should be received by all subscribers
                var messageIds = messages.Select(m => m.MessageId).ToHashSet();
                var allMessagesReceived = messageIds.All(id => 
                    receivedMessages.Count(m => m.MessageId == id) == subscriberCount);

                return receivedCount == expectedTotal && allMessagesReceived;
            });
    }

    /// <summary>
    /// Property 8 (No Subscribers): Transport with No Subscribers Should Not Throw
    /// 
    /// *For any* published message with no active subscribers, the publish operation 
    /// SHALL complete successfully without throwing an exception.
    /// 
    /// **Validates: Requirements 4.17**
    /// 
    /// Feature: tdd-validation, Property 8: Transport Delivery Guarantee (No Subscribers)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property Transport_PublishNoSubscribers_DoesNotThrow()
    {
        return Prop.ForAll(
            MessageGenerators.TestMessageArbitrary(),
            (testMessage) =>
            {
                // Arrange - Create a fresh transport with no subscribers
                var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());

                // Create a message based on the generated test message
                var message = new TransportPropertyTestMessage
                {
                    MessageId = testMessage.MessageId,
                    Content = testMessage.Content,
                    Timestamp = testMessage.Timestamp
                };

                // Act & Assert - Should not throw
                try
                {
                    transport.PublishAsync(message).GetAwaiter().GetResult();
                    return true;
                }
                catch
                {
                    return false;
                }
            });
    }

    /// <summary>
    /// Property 9: Transport Message Ordering
    /// 
    /// *For any* sequence of messages published to a topic, subscribers SHALL receive 
    /// messages in the same order they were published.
    /// 
    /// **Validates: Requirements 4.9, 4.10**
    /// 
    /// Feature: tdd-validation, Property 9: Transport Message Ordering
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property Transport_Messages_DeliveredInOrder()
    {
        return Prop.ForAll(
            Gen.Choose(2, 10).ToArbitrary(), // Number of messages (2-10)
            (messageCount) =>
            {
                // Arrange - Create a fresh transport for each test iteration
                // Note: We need a fresh transport to avoid interference from previous tests
                // since TypedSubscribers is static
                var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
                var receivedMessages = new List<OrderedTestMessage>();
                var lockObj = new object();

                // Subscribe a single handler that records messages in order
                transport.SubscribeAsync<OrderedTestMessage>((msg, ctx) =>
                {
                    lock (lockObj)
                    {
                        receivedMessages.Add(msg);
                    }
                    return Task.CompletedTask;
                }).GetAwaiter().GetResult();

                // Create messages with sequential order numbers
                var messages = new List<OrderedTestMessage>();
                for (int i = 0; i < messageCount; i++)
                {
                    var msgId = MessageExtensions.NewMessageId();
                    messages.Add(new OrderedTestMessage
                    {
                        MessageId = msgId,
                        SequenceNumber = i,
                        Content = $"Message-{i}"
                    });
                }

                // Act - Publish messages sequentially (synchronously to ensure order)
                foreach (var msg in messages)
                {
                    transport.PublishAsync(msg).GetAwaiter().GetResult();
                }

                // Allow time for async delivery to complete
                Task.Delay(100).GetAwaiter().GetResult();

                // Assert - Messages should be received in the same order they were published
                if (receivedMessages.Count != messageCount)
                    return false;

                // Check that sequence numbers match the order of publishing
                for (int i = 0; i < messageCount; i++)
                {
                    if (receivedMessages[i].SequenceNumber != i)
                        return false;
                }

                return true;
            });
    }

    /// <summary>
    /// Property 9 (Alternative): Transport Message Ordering with Single Publisher FIFO
    /// 
    /// *For any* sequence of messages published by a single publisher, 
    /// the subscriber SHALL receive messages in FIFO order.
    /// 
    /// **Validates: Requirements 4.9, 4.10**
    /// 
    /// Feature: tdd-validation, Property 9: Transport Message Ordering (FIFO)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.QuickMaxTest)]
    public Property Transport_SinglePublisher_MaintainsFIFOOrder()
    {
        return Prop.ForAll(
            Gen.Choose(3, 15).ToArbitrary(), // Number of messages (3-15)
            (messageCount) =>
            {
                // Arrange
                var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
                var receivedIds = new List<long>();
                var lockObj = new object();

                transport.SubscribeAsync<FifoTestMessage>((msg, ctx) =>
                {
                    lock (lockObj)
                    {
                        receivedIds.Add(msg.MessageId);
                    }
                    return Task.CompletedTask;
                }).GetAwaiter().GetResult();

                // Create messages with unique IDs
                var publishedIds = new List<long>();
                for (int i = 0; i < messageCount; i++)
                {
                    var msgId = MessageExtensions.NewMessageId();
                    publishedIds.Add(msgId);
                }

                // Act - Publish messages in order
                foreach (var msgId in publishedIds)
                {
                    var msg = new FifoTestMessage
                    {
                        MessageId = msgId,
                        Content = $"FIFO-{msgId}"
                    };
                    transport.PublishAsync(msg).GetAwaiter().GetResult();
                }

                // Allow delivery
                Task.Delay(100).GetAwaiter().GetResult();

                // Assert - Received order should match published order
                return publishedIds.SequenceEqual(receivedIds);
            });
    }
}


/// <summary>
/// 用于 Transport 属性测试的测试消息
/// 使用 QoS AtLeastOnce 和 WaitForResult 确保同步交付
/// </summary>
[MemoryPackable]
public partial record TransportPropertyTestMessage : IMessage
{
    public required long MessageId { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    
    // Use AtLeastOnce with WaitForResult for reliable delivery in tests
    public QualityOfService QoS => QualityOfService.AtLeastOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

/// <summary>
/// 用于消息顺序属性测试的测试消息
/// 包含序列号以验证顺序
/// </summary>
[MemoryPackable]
public partial record OrderedTestMessage : IMessage
{
    public required long MessageId { get; init; }
    public required int SequenceNumber { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    
    public QualityOfService QoS => QualityOfService.AtLeastOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}

/// <summary>
/// 用于 FIFO 顺序属性测试的测试消息
/// </summary>
[MemoryPackable]
public partial record FifoTestMessage : IMessage
{
    public required long MessageId { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    
    public QualityOfService QoS => QualityOfService.AtLeastOnce;
    public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
}
