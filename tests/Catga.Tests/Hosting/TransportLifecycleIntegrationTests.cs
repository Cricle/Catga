using Catga.Abstractions;
using Catga.Core;
using Catga.Hosting;
using Catga.Resilience;
using Catga.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// 传输层生命周期集成测试
/// 测试完整的启动-运行-停机流程
/// </summary>
public class TransportLifecycleIntegrationTests
{
    private class TestMessage : IMessage
    {
        public long MessageId { get; set; }
        public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
        public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.WaitForResult;
        public string Content { get; set; } = string.Empty;
    }

    [Fact]
    public async Task InMemoryTransport_CompleteLifecycle_WorksCorrectly()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var transport = new InMemoryMessageTransport(NullLogger<InMemoryMessageTransport>.Instance, provider);

        // Act & Assert - Initialize
        await transport.InitializeAsync();
        Assert.True(transport.IsHealthy);
        Assert.True(transport.IsAcceptingMessages);
        Assert.Equal(0, transport.PendingOperations);

        // Act & Assert - Subscribe and Publish
        var received = new List<TestMessage>();
        await transport.SubscribeAsync<TestMessage>((msg, ctx) =>
        {
            received.Add(msg);
            return Task.CompletedTask;
        });

        var message = new TestMessage { MessageId = 1, Content = "Test" };
        await transport.PublishAsync(message);

        // Wait a bit for async processing
        await Task.Delay(100);
        Assert.Single(received);
        Assert.Equal("Test", received[0].Content);

        // Act & Assert - Stop accepting messages
        transport.StopAcceptingMessages();
        Assert.False(transport.IsAcceptingMessages);

        // Should throw when trying to publish after stopping
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.PublishAsync(new TestMessage { MessageId = 2, Content = "Should fail" });
        });

        // Act & Assert - Wait for completion
        await transport.WaitForCompletionAsync();
        Assert.Equal(0, transport.PendingOperations);
    }

    [Fact]
    public async Task InMemoryTransport_WaitForCompletion_WaitsForPendingOperations()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var transport = new InMemoryMessageTransport(NullLogger<InMemoryMessageTransport>.Instance, provider);
        await transport.InitializeAsync();

        var processingStarted = new TaskCompletionSource<bool>();
        var continueProcessing = new TaskCompletionSource<bool>();
        var handlerCalled = 0;

        await transport.SubscribeAsync<TestMessage>(async (msg, ctx) =>
        {
            if (Interlocked.Increment(ref handlerCalled) == 1)
            {
                processingStarted.TrySetResult(true);
            }
            await continueProcessing.Task;
        });

        // Act - Start publishing (will be pending)
        var publishTask = Task.Run(async () =>
        {
            await transport.PublishAsync(new TestMessage { MessageId = 1, Content = "Test" });
        });

        // Wait for processing to start
        await processingStarted.Task;

        // Verify there's a pending operation
        Assert.True(transport.PendingOperations > 0);

        // Start waiting for completion
        var waitTask = transport.WaitForCompletionAsync();

        // Complete the processing
        continueProcessing.TrySetResult(true);
        await publishTask;

        // Wait should complete
        await waitTask;
        Assert.Equal(0, transport.PendingOperations);
    }

    [Fact]
    public async Task InMemoryTransport_HealthCheck_ReflectsState()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var transport = new InMemoryMessageTransport(NullLogger<InMemoryMessageTransport>.Instance, provider);

        // Act & Assert - Before initialization
        Assert.True(transport.IsHealthy); // InMemory is always healthy
        Assert.Null(transport.LastHealthCheck);

        // After initialization
        await transport.InitializeAsync();
        Assert.True(transport.IsHealthy);
        Assert.NotNull(transport.LastHealthCheck);
        Assert.NotNull(transport.HealthStatus);
    }

    [Fact]
    public async Task InMemoryTransport_GracefulShutdown_CompletesInFlightMessages()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var transport = new InMemoryMessageTransport(NullLogger<InMemoryMessageTransport>.Instance, provider);
        await transport.InitializeAsync();

        var messagesProcessed = new List<int>();
        var processingDelay = TimeSpan.FromMilliseconds(50);

        await transport.SubscribeAsync<TestMessage>(async (msg, ctx) =>
        {
            await Task.Delay(processingDelay);
            lock (messagesProcessed)
            {
                messagesProcessed.Add((int)msg.MessageId);
            }
        });

        // Act - Publish multiple messages
        var publishTasks = new List<Task>();
        for (int i = 1; i <= 5; i++)
        {
            var msg = new TestMessage { MessageId = i, Content = $"Message {i}" };
            publishTasks.Add(transport.PublishAsync(msg));
        }

        // Wait for all publishes to start
        await Task.WhenAll(publishTasks);

        // Stop accepting new messages
        transport.StopAcceptingMessages();

        // Wait for completion
        await transport.WaitForCompletionAsync();

        // Assert - All messages should be processed
        Assert.Equal(5, messagesProcessed.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, messagesProcessed.OrderBy(x => x));
    }

    [Fact]
    public async Task InMemoryTransport_MultipleSubscribers_AllReceiveMessages()
    {
        // Arrange
        var provider = new DefaultResiliencePipelineProvider();
        var transport = new InMemoryMessageTransport(NullLogger<InMemoryMessageTransport>.Instance, provider);
        await transport.InitializeAsync();

        var subscriber1Messages = new List<TestMessage>();
        var subscriber2Messages = new List<TestMessage>();

        await transport.SubscribeAsync<TestMessage>((msg, ctx) =>
        {
            subscriber1Messages.Add(msg);
            return Task.CompletedTask;
        });

        await transport.SubscribeAsync<TestMessage>((msg, ctx) =>
        {
            subscriber2Messages.Add(msg);
            return Task.CompletedTask;
        });

        // Act
        var message = new TestMessage { MessageId = 1, Content = "Broadcast" };
        await transport.PublishAsync(message);

        // Wait for async processing
        await Task.Delay(100);

        // Assert - Both subscribers should receive the message
        Assert.Single(subscriber1Messages);
        Assert.Single(subscriber2Messages);
        Assert.Equal("Broadcast", subscriber1Messages[0].Content);
        Assert.Equal("Broadcast", subscriber2Messages[0].Content);
    }
}
