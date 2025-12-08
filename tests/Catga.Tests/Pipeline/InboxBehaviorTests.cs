using Catga.Abstractions;
using Catga.Core;
using Catga.Inbox;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Pipeline;

/// <summary>
/// InboxBehavior单元测试
/// 目标覆盖率: 从 0% → 90%+
/// </summary>
public class InboxBehaviorTests
{
    private readonly IInboxStore _mockStore;
    private readonly IMessageSerializer _mockSerializer;
    private readonly ILogger<InboxBehavior<TestRequest, TestResponse>> _mockLogger;
    private readonly InboxBehavior<TestRequest, TestResponse> _behavior;

    public InboxBehaviorTests()
    {
        _mockStore = Substitute.For<IInboxStore>();
        _mockSerializer = Substitute.For<IMessageSerializer>();
        _mockLogger = Substitute.For<ILogger<InboxBehavior<TestRequest, TestResponse>>>();
        _behavior = new InboxBehavior<TestRequest, TestResponse>(
            _mockLogger,
            _mockStore,
            _mockSerializer);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new InboxBehavior<TestRequest, TestResponse>(
            null!,
            _mockStore,
            _mockSerializer);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullPersistence_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new InboxBehavior<TestRequest, TestResponse>(
            _mockLogger,
            null!,
            _mockSerializer);
        act.Should().Throw<ArgumentNullException>().WithParameterName("persistence");
    }

    [Fact]
    public void Constructor_WithNullSerializer_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new InboxBehavior<TestRequest, TestResponse>(
            _mockLogger,
            _mockStore,
            null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    #endregion

    #region Message Without MessageId Tests

    [Fact]
    public async Task HandleAsync_WithZeroMessageId_ShouldSkipInboxCheck()
    {
        // Arrange
        var request = new TestRequest { MessageId = 0 };
        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(new TestResponse { Result = "OK" }));

        // Act
        var result = await _behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _mockStore.DidNotReceive().HasBeenProcessedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Already Processed Tests

    [Fact]
    public async Task HandleAsync_MessageAlreadyProcessed_ShouldReturnCachedResult()
    {
        // Arrange
        var request = new TestRequest { MessageId = 123 };
        var cachedResponse = new TestResponse { Result = "Cached" };
        var cachedResult = CatgaResult<TestResponse>.Success(cachedResponse);
        var serializedBytes = new byte[] { 1, 2, 3 };

        _mockStore.HasBeenProcessedAsync(123, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        _mockStore.GetProcessedResultAsync(123, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<byte[]?>(serializedBytes));
        _mockSerializer.Deserialize<CatgaResult<TestResponse>>(serializedBytes)
            .Returns(cachedResult);

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Should not be called");

        // Act
        var result = await _behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _mockStore.Received(1).HasBeenProcessedAsync(123, Arg.Any<CancellationToken>());
        await _mockStore.DidNotReceive().TryLockMessageAsync(Arg.Any<long>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CachedResultEmpty_ShouldReturnDefaultSuccess()
    {
        // Arrange
        var request = new TestRequest { MessageId = 456 };

        _mockStore.HasBeenProcessedAsync(456, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        _mockStore.GetProcessedResultAsync(456, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<byte[]?>(Array.Empty<byte>()));

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Should not be called");

        // Act
        var result = await _behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_CachedResultInvalid_ShouldReturnDefaultSuccess()
    {
        // Arrange
        var request = new TestRequest { MessageId = 789 };

        _mockStore.HasBeenProcessedAsync(789, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        _mockStore.GetProcessedResultAsync(789, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<byte[]?>(new byte[] { 0xFF }));

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Should not be called");

        // Act
        var result = await _behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Lock Acquisition Tests

    [Fact]
    public async Task HandleAsync_LockAcquisitionFailed_ShouldReturnFailureWithRetry()
    {
        // Arrange
        var request = new TestRequest { MessageId = 999 };

        _mockStore.HasBeenProcessedAsync(999, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        _mockStore.TryLockMessageAsync(999, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Should not be called");

        // Act
        var result = await _behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("another instance");
        result.ErrorCode.Should().Be(ErrorCodes.LockFailed);
    }

    #endregion

    #region Successful Processing Tests

    [Fact]
    public async Task HandleAsync_FirstTimeProcessing_ShouldExecuteAndStoreResult()
    {
        // Arrange
        var request = new TestRequest { MessageId = 111, Data = "test" };
        var response = new TestResponse { Result = "OK" };

        _mockStore.HasBeenProcessedAsync(111, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        _mockStore.TryLockMessageAsync(111, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(response));

        // Act
        var result = await _behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(response);
        await _mockStore.Received(1).MarkAsProcessedAsync(
            Arg.Is<InboxMessage>(m => m.MessageId == 111),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SuccessfulProcessing_ShouldStoreCorrectInboxMessage()
    {
        // Arrange
        var request = new TestRequest { MessageId = 222, Data = "data", CorrelationId = 333 };
        var response = new TestResponse { Result = "Success" };

        _mockStore.HasBeenProcessedAsync(222, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        _mockStore.TryLockMessageAsync(222, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        InboxMessage? capturedMessage = null;
        _mockStore.MarkAsProcessedAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(x => capturedMessage = x.Arg<InboxMessage>());

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(response));

        // Act
        await _behavior.HandleAsync(request, next);

        // Assert
        capturedMessage.Should().NotBeNull();
        capturedMessage!.MessageId.Should().Be(222);
        capturedMessage.CorrelationId.Should().Be(333);
        capturedMessage.MessageType.Should().Contain("TestRequest");
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task HandleAsync_HandlerThrowsException_ShouldReleaseLockAndReturnFailure()
    {
        // Arrange
        var request = new TestRequest { MessageId = 444 };
        var expectedException = new InvalidOperationException("Test error");

        _mockStore.HasBeenProcessedAsync(444, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        _mockStore.TryLockMessageAsync(444, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        PipelineDelegate<TestResponse> next = () => throw expectedException;

        // Act
        var result = await _behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Test error");
        await _mockStore.Received(1).ReleaseLockAsync(444, Arg.Any<CancellationToken>());
        await _mockStore.DidNotReceive().MarkAsProcessedAsync(Arg.Any<InboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PersistenceThrowsException_ShouldReturnFailure()
    {
        // Arrange
        var request = new TestRequest { MessageId = 555 };

        _mockStore.HasBeenProcessedAsync(555, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException<bool>(new InvalidOperationException("Database error")));

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(new TestResponse()));

        // Act
        var result = await _behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Database error");
        result.ErrorCode.Should().Be(ErrorCodes.PersistenceFailed);
    }

    #endregion

    #region Custom Lock Duration Tests

    [Fact]
    public async Task HandleAsync_WithCustomLockDuration_ShouldUseSpecifiedDuration()
    {
        // Arrange
        var customDuration = TimeSpan.FromMinutes(10);
        var behavior = new InboxBehavior<TestRequest, TestResponse>(
            _mockLogger,
            _mockStore,
            _mockSerializer,
            customDuration);

        var request = new TestRequest { MessageId = 666 };

        _mockStore.HasBeenProcessedAsync(666, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));
        _mockStore.TryLockMessageAsync(666, customDuration, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(new TestResponse()));

        // Act
        await behavior.HandleAsync(request, next);

        // Assert
        await _mockStore.Received(1).TryLockMessageAsync(666, customDuration, Arg.Any<CancellationToken>());
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task HandleAsync_WithCancellationToken_ShouldPassToStore()
    {
        // Arrange
        var request = new TestRequest { MessageId = 777 };
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        _mockStore.HasBeenProcessedAsync(777, cancellationToken)
            .Returns(ValueTask.FromResult(false));
        _mockStore.TryLockMessageAsync(777, Arg.Any<TimeSpan>(), cancellationToken)
            .Returns(ValueTask.FromResult(true));

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(new TestResponse()));

        // Act
        await _behavior.HandleAsync(request, next, cancellationToken);

        // Assert
        await _mockStore.Received(1).HasBeenProcessedAsync(777, cancellationToken);
        await _mockStore.Received(1).TryLockMessageAsync(777, Arg.Any<TimeSpan>(), cancellationToken);
    }

    #endregion

    #region Test Helper Classes

    public class TestRequest : IRequest<TestResponse>, IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public class TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }

    #endregion
}







