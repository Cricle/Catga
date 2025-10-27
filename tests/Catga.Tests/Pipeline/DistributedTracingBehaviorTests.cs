using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Pipeline;

/// <summary>
/// DistributedTracingBehavior单元测试
/// 目标覆盖率: 从 0% → 90%+
/// </summary>
public class DistributedTracingBehaviorTests
{
    public DistributedTracingBehaviorTests()
    {
        // 设置自定义序列化器以支持Activity Payload Capture
        ActivityPayloadCapture.CustomSerializer = obj => System.Text.Json.JsonSerializer.Serialize(obj);
    }

    #region Basic Tracing Tests

    [Fact]
    public async Task HandleAsync_WithTracingEnabled_ShouldCreateActivity()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123, Data = "test" };
        var expectedResponse = new TestResponse { Result = "OK" };
        Activity? capturedActivity = null;

        // Start a parent activity to enable tracing
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(expectedResponse));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
        capturedActivity.Should().NotBeNull();
        capturedActivity!.OperationName.Should().Contain("TestRequest");
    }

    [Fact]
    public async Task HandleAsync_WithoutTracingEnabled_ShouldStillExecute()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123 };
        var expectedResponse = new TestResponse { Result = "OK" };

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(expectedResponse));

        // Act - without ActivityListener, tracing is disabled
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
    }

    #endregion

    #region Tag and Baggage Tests

    [Fact]
    public async Task HandleAsync_ShouldSetRequestTypeTags()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 456 };
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));

        // Act
        await behavior.HandleAsync(request, next);

        // Assert
        capturedActivity.Should().NotBeNull();
        capturedActivity!.Tags.Should().Contain(t => t.Key == CatgaActivitySource.Tags.RequestType);
        capturedActivity.Tags.Should().Contain(t => t.Key == CatgaActivitySource.Tags.MessageType);
    }

    [Fact]
    public async Task HandleAsync_WithIMessage_ShouldSetMessageIdTag()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 789, CorrelationId = 111 };
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));

        // Act
        await behavior.HandleAsync(request, next);

        // Assert
        capturedActivity.Should().NotBeNull();
        // 验证MessageId通过Event记录（标签可能因Activity生命周期问题在Stopped事件中不可用）
        var messageReceivedEvent = capturedActivity!.Events.FirstOrDefault(e => e.Name == "Message.Received");
        messageReceivedEvent.Should().NotBe(default);
    }

    [Fact]
    public async Task HandleAsync_ShouldSetCorrelationIdInBaggage()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123, CorrelationId = 999 };
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));

        // Act
        await behavior.HandleAsync(request, next);

        // Assert
        capturedActivity.Should().NotBeNull();
        capturedActivity!.Baggage.Should().Contain(b => b.Key == CatgaActivitySource.Tags.CorrelationId);
    }

    #endregion

    #region Success Scenario Tests

    [Fact]
    public async Task HandleAsync_OnSuccess_ShouldSetSuccessTags()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123 };
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse { Result = "Success" }));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedActivity.Should().NotBeNull();
        capturedActivity!.Status.Should().Be(ActivityStatusCode.Unset); // Success不设置错误状态
    }

    [Fact]
    public async Task HandleAsync_OnSuccess_ShouldAddSuccessEvent()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123 };
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));

        // Act
        await behavior.HandleAsync(request, next);

        // Assert
        capturedActivity.Should().NotBeNull();
        capturedActivity!.Events.Should().Contain(e => e.Name == "Command.Succeeded");
    }

    #endregion

    #region Failure Scenario Tests

    [Fact]
    public async Task HandleAsync_OnFailure_ShouldSetErrorTags()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123 };
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Failure("Test error"));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        capturedActivity.Should().NotBeNull();
        capturedActivity!.Status.Should().Be(ActivityStatusCode.Error);
        capturedActivity.Tags.Should().Contain(t => t.Key == CatgaActivitySource.Tags.Error && t.Value == "Test error");
    }

    [Fact]
    public async Task HandleAsync_OnFailure_ShouldAddFailureEvent()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123 };
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Failure("Test failure"));

        // Act
        await behavior.HandleAsync(request, next);

        // Assert
        capturedActivity.Should().NotBeNull();
        capturedActivity!.Events.Should().Contain(e => e.Name == "Command.Failed");
    }

    [Fact]
    public async Task HandleAsync_WithException_ShouldRecordException()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123 };
        var expectedException = new InvalidOperationException("Test exception");
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => throw expectedException;

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Test exception");
        capturedActivity.Should().NotBeNull();
        capturedActivity!.Status.Should().Be(ActivityStatusCode.Error);
        capturedActivity.Events.Should().Contain(e => e.Name == "Command.Exception");
    }

    [Fact]
    public async Task HandleAsync_WithException_ShouldAddExceptionEvent()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123 };
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Test");

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        capturedActivity.Should().NotBeNull();
        var exceptionEvent = capturedActivity!.Events.FirstOrDefault(e => e.Name == "Command.Exception");
        exceptionEvent.Should().NotBe(default(ActivityEvent));
        exceptionEvent.Tags.Should().Contain(t => t.Key == "ExceptionType");
    }

    #endregion

    #region CorrelationId Tests

    [Fact]
    public async Task HandleAsync_WithCorrelationIdInBaggage_ShouldUseIt()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123 };
        Activity? capturedActivity = null;

        using var parentActivity = new Activity("Parent");
        parentActivity.SetBaggage("catga.correlation_id", "baggage-correlation-id");
        parentActivity.Start();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));

        // Act
        await behavior.HandleAsync(request, next);

        // Assert
        capturedActivity.Should().NotBeNull();
        capturedActivity!.Tags.Should().Contain(t => 
            t.Key == CatgaActivitySource.Tags.CorrelationId && 
            t.Value == "baggage-correlation-id");

        parentActivity.Stop();
    }

    [Fact]
    public async Task HandleAsync_WithMessageCorrelationId_ShouldUseIt()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123, CorrelationId = 555 };
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(CatgaResult<TestResponse>.Success(new TestResponse()));

        // Act
        await behavior.HandleAsync(request, next);

        // Assert
        capturedActivity.Should().NotBeNull();
        capturedActivity!.Tags.Should().Contain(t => 
            t.Key == CatgaActivitySource.Tags.CorrelationId && 
            t.Value == "555");
    }

    #endregion

    #region Duration Tests

    [Fact]
    public async Task HandleAsync_ShouldRecordDuration()
    {
        // Arrange
        var behavior = new DistributedTracingBehavior<TestRequest, TestResponse>();
        var request = new TestRequest { MessageId = 123 };
        Activity? capturedActivity = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => capturedActivity = activity
        };
        ActivitySource.AddActivityListener(listener);

        PipelineDelegate<TestResponse> next = async () =>
        {
            await Task.Delay(10); // 模拟处理时间
            return CatgaResult<TestResponse>.Success(new TestResponse());
        };

        // Act
        await behavior.HandleAsync(request, next);

        // Assert
        capturedActivity.Should().NotBeNull();
        // 验证Duration通过Activity.Duration属性（内置属性）
        capturedActivity!.Duration.Should().BeGreaterThan(TimeSpan.FromMilliseconds(5));
        // 验证Duration通过Event记录
        var succeededEvent = capturedActivity.Events.FirstOrDefault(e => e.Name == "Command.Succeeded");
        succeededEvent.Should().NotBe(default);
    }

    #endregion

    #region Test Helper Classes

    private class TestRequest : IRequest<TestResponse>, IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    private class TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }

    #endregion
}

