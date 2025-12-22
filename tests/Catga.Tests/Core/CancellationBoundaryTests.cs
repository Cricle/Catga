using Catga.Abstractions;
using Catga.Core;
using Catga.EventSourcing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Catga.Transport;
using FluentAssertions;
using MemoryPack;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// 取消边界测试
/// 验证 EventStore 和 Transport 在取消场景下的正确行为
/// 
/// **Validates: Requirements 8.10-8.14**
/// </summary>
[Trait("Category", "Boundary")]
[Trait("Category", "Cancellation")]
public partial class CancellationBoundaryTests
{
    #region EventStore Cancellation Tests (Requirements 8.10-8.12)

    /// <summary>
    /// Tests that EventStore.AppendAsync throws OperationCanceledException when 
    /// the cancellation token is already cancelled before the operation starts.
    /// 
    /// **Validates: Requirements 8.10**
    /// </summary>
    [Fact]
    public async Task EventStore_Append_AlreadyCancelled_ThrowsOperationCancelled()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = "test-stream";
        var events = new List<IEvent> { new TestCancellationEvent("test") };
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = async () => await store.AppendAsync(streamId, events, ct: cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that EventStore.ReadAsync throws OperationCanceledException when 
    /// the cancellation token is already cancelled before the operation starts.
    /// 
    /// **Validates: Requirements 8.11**
    /// </summary>
    [Fact]
    public async Task EventStore_Read_AlreadyCancelled_ThrowsOperationCancelled()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = "test-stream";
        
        // Pre-populate with some events
        await store.AppendAsync(streamId, [new TestCancellationEvent("test")]);
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = async () => await store.ReadAsync(streamId, ct: cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that EventStore.GetVersionAsync throws OperationCanceledException when 
    /// the cancellation token is already cancelled before the operation starts.
    /// 
    /// **Validates: Requirements 8.12**
    /// </summary>
    [Fact]
    public async Task EventStore_GetVersion_AlreadyCancelled_ThrowsOperationCancelled()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = "test-stream";
        
        // Pre-populate with some events
        await store.AppendAsync(streamId, [new TestCancellationEvent("test")]);
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = async () => await store.GetVersionAsync(streamId, ct: cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that EventStore.GetAllStreamIdsAsync throws OperationCanceledException when 
    /// the cancellation token is already cancelled before the operation starts.
    /// 
    /// **Validates: Requirements 8.12**
    /// </summary>
    [Fact]
    public async Task EventStore_GetAllStreamIds_AlreadyCancelled_ThrowsOperationCancelled()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        
        // Pre-populate with some events
        await store.AppendAsync("stream-1", [new TestCancellationEvent("test")]);
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = async () => await store.GetAllStreamIdsAsync(ct: cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that EventStore.ReadToVersionAsync throws OperationCanceledException when 
    /// the cancellation token is already cancelled before the operation starts.
    /// 
    /// **Validates: Requirements 8.12**
    /// </summary>
    [Fact]
    public async Task EventStore_ReadToVersion_AlreadyCancelled_ThrowsOperationCancelled()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = "test-stream";
        
        // Pre-populate with some events
        await store.AppendAsync(streamId, [new TestCancellationEvent("test")]);
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = async () => await store.ReadToVersionAsync(streamId, 0, ct: cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that EventStore.ReadToTimestampAsync throws OperationCanceledException when 
    /// the cancellation token is already cancelled before the operation starts.
    /// 
    /// **Validates: Requirements 8.12**
    /// </summary>
    [Fact]
    public async Task EventStore_ReadToTimestamp_AlreadyCancelled_ThrowsOperationCancelled()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = "test-stream";
        
        // Pre-populate with some events
        await store.AppendAsync(streamId, [new TestCancellationEvent("test")]);
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = async () => await store.ReadToTimestampAsync(streamId, DateTime.UtcNow, ct: cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that EventStore.GetVersionHistoryAsync throws OperationCanceledException when 
    /// the cancellation token is already cancelled before the operation starts.
    /// 
    /// **Validates: Requirements 8.12**
    /// </summary>
    [Fact]
    public async Task EventStore_GetVersionHistory_AlreadyCancelled_ThrowsOperationCancelled()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = "test-stream";
        
        // Pre-populate with some events
        await store.AppendAsync(streamId, [new TestCancellationEvent("test")]);
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var act = async () => await store.GetVersionHistoryAsync(streamId, ct: cts.Token);
        
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that EventStore operations complete successfully when the cancellation 
    /// token is not cancelled.
    /// 
    /// **Validates: Requirements 8.10-8.12**
    /// </summary>
    [Fact]
    public async Task EventStore_Operations_WithValidToken_Succeed()
    {
        // Arrange
        var store = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        var streamId = "test-stream";
        using var cts = new CancellationTokenSource();

        // Act & Assert - All operations should succeed with a valid (non-cancelled) token
        await store.AppendAsync(streamId, [new TestCancellationEvent("test1")], ct: cts.Token);
        
        var result = await store.ReadAsync(streamId, ct: cts.Token);
        result.Events.Should().HaveCount(1);
        
        var version = await store.GetVersionAsync(streamId, ct: cts.Token);
        version.Should().Be(0);
        
        var streams = await store.GetAllStreamIdsAsync(ct: cts.Token);
        streams.Should().Contain(streamId);
    }

    #endregion

    #region Transport Cancellation Tests (Requirements 8.13-8.14)

    /// <summary>
    /// Tests that Transport.PublishAsync completes without error when there are no subscribers,
    /// even with a cancelled token (no work to cancel).
    /// 
    /// Note: The InMemoryMessageTransport implementation does not check cancellation token
    /// at the start of the operation - it only passes it to the resilience pipeline.
    /// This is a documentation test of the actual behavior.
    /// 
    /// **Validates: Requirements 8.13**
    /// </summary>
    [Fact]
    public async Task Transport_Publish_NoSubscribers_AlreadyCancelled_ReturnsImmediately()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
        var message = new TestCancellationMessage(123, "Test");
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Should not throw because there's no work to do
        var act = async () => await transport.PublishAsync(message, cancellationToken: cts.Token);
        
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Tests that Transport operations complete successfully when the cancellation 
    /// token is not cancelled.
    /// 
    /// **Validates: Requirements 8.13-8.14**
    /// </summary>
    [Fact]
    public async Task Transport_Operations_WithValidToken_Succeed()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
        var received = false;
        
        await transport.SubscribeAsync<TestCancellationMessage>((msg, ctx) =>
        {
            received = true;
            return Task.CompletedTask;
        });

        var message = new TestCancellationMessage(789, "Test");
        using var cts = new CancellationTokenSource();

        // Act
        await transport.PublishAsync(message, cancellationToken: cts.Token);
        await Task.Delay(50); // Allow async processing

        // Assert
        received.Should().BeTrue();
    }

    /// <summary>
    /// Tests that Transport.SubscribeAsync completes successfully with a valid token.
    /// 
    /// **Validates: Requirements 8.13**
    /// </summary>
    [Fact]
    public async Task Transport_Subscribe_WithValidToken_Succeeds()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
        using var cts = new CancellationTokenSource();

        // Act & Assert
        var act = async () => await transport.SubscribeAsync<TestCancellationMessage>(
            (msg, ctx) => Task.CompletedTask, 
            cancellationToken: cts.Token);
        
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Tests that Transport.PublishBatchAsync completes without error when there are no subscribers.
    /// 
    /// **Validates: Requirements 8.13**
    /// </summary>
    [Fact]
    public async Task Transport_PublishBatch_NoSubscribers_AlreadyCancelled_ReturnsImmediately()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
        var messages = new[]
        {
            new TestCancellationMessage(1, "Test1"),
            new TestCancellationMessage(2, "Test2")
        };
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Should not throw because there's no work to do
        var act = async () => await transport.PublishBatchAsync(messages, cancellationToken: cts.Token);
        
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Tests that Transport.SendBatchAsync completes without error when there are no subscribers.
    /// 
    /// **Validates: Requirements 8.14**
    /// </summary>
    [Fact]
    public async Task Transport_SendBatch_NoSubscribers_AlreadyCancelled_ReturnsImmediately()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
        var messages = new[]
        {
            new TestCancellationMessage(3, "Test3"),
            new TestCancellationMessage(4, "Test4")
        };
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Should not throw because there's no work to do
        var act = async () => await transport.SendBatchAsync(messages, "destination", cancellationToken: cts.Token);
        
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Tests that Transport.SendAsync completes without error when there are no subscribers.
    /// 
    /// **Validates: Requirements 8.14**
    /// </summary>
    [Fact]
    public async Task Transport_Send_NoSubscribers_AlreadyCancelled_ReturnsImmediately()
    {
        // Arrange
        var transport = new InMemoryMessageTransport(null, new DiagnosticResiliencePipelineProvider());
        var message = new TestCancellationMessage(456, "Test");
        
        // Create an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - Should not throw because there's no work to do
        var act = async () => await transport.SendAsync(message, "destination", cancellationToken: cts.Token);
        
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Test Helpers

    private record TestCancellationEvent(string Data) : IEvent
    {
        private static long _counter;
        public long MessageId { get; init; } = Interlocked.Increment(ref _counter);
    }

    [MemoryPackable]
    public partial record TestCancellationMessage(int Id, string Name) : IMessage
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
        public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
    }

    #endregion
}
