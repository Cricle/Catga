using Catga.Abstractions;
using Catga.Core;
using Catga.Inbox;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Catga.Tests.Pipeline;

/// <summary>
/// Comprehensive tests for InboxBehavior
/// </summary>
public class InboxBehaviorTests
{
    private readonly IInboxStore _inboxStore;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<InboxBehavior<TestInboxRequest, string>> _logger;
    private readonly InboxBehavior<TestInboxRequest, string> _behavior;

    public InboxBehaviorTests()
    {
        _inboxStore = Substitute.For<IInboxStore>();
        _serializer = Substitute.For<IMessageSerializer>();
        _logger = Substitute.For<ILogger<InboxBehavior<TestInboxRequest, string>>>();
        
        _behavior = new InboxBehavior<TestInboxRequest, string>(
            _logger,
            _inboxStore,
            _serializer);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        var act = () => new InboxBehavior<TestInboxRequest, string>(
            null!,
            _inboxStore,
            _serializer);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullPersistence_ShouldThrow()
    {
        var act = () => new InboxBehavior<TestInboxRequest, string>(
            _logger,
            null!,
            _serializer);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("persistence");
    }

    [Fact]
    public void Constructor_WithNullSerializer_ShouldThrow()
    {
        var act = () => new InboxBehavior<TestInboxRequest, string>(
            _logger,
            _inboxStore,
            null!);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serializer");
    }

    [Fact]
    public void Constructor_WithOptions_ShouldUseLockDuration()
    {
        var options = Options.Create(new InboxBehaviorOptions
        {
            LockDuration = TimeSpan.FromMinutes(10)
        });
        
        var behavior = new InboxBehavior<TestInboxRequest, string>(
            _logger,
            _inboxStore,
            _serializer,
            options);
        
        behavior.Should().NotBeNull();
    }

    #endregion

    #region HandleAsync Tests - No MessageId

    [Fact]
    public async Task HandleAsync_WithNoMessageId_ShouldCallNext()
    {
        var request = new TestInboxRequest { MessageId = 0 };
        var expectedResult = CatgaResult<string>.Success("result");
        var nextCalled = false;
        
        PipelineDelegate<string> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(expectedResult);
        };
        
        var result = await _behavior.HandleAsync(request, next);
        
        nextCalled.Should().BeTrue();
        result.Should().Be(expectedResult);
    }

    #endregion

    #region HandleAsync Tests - Already Processed

    [Fact]
    public async Task HandleAsync_WhenAlreadyProcessed_ShouldReturnCachedResult()
    {
        var request = new TestInboxRequest { MessageId = 123 };
        var cachedResult = CatgaResult<string>.Success("cached");
        var cachedBytes = new byte[] { 1, 2, 3 };
        
        _inboxStore.HasBeenProcessedAsync(123, Arg.Any<CancellationToken>())
            .Returns(true);
        _inboxStore.GetProcessedResultAsync(123, Arg.Any<CancellationToken>())
            .Returns(cachedBytes);
        _serializer.Deserialize<CatgaResult<string>>(cachedBytes)
            .Returns(cachedResult);
        
        var nextCalled = false;
        PipelineDelegate<string> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(CatgaResult<string>.Success("new"));
        };
        
        var result = await _behavior.HandleAsync(request, next);
        
        nextCalled.Should().BeFalse();
        result.Should().Be(cachedResult);
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyProcessedButNoCachedResult_ShouldReturnSuccessWithDefault()
    {
        var request = new TestInboxRequest { MessageId = 123 };
        
        _inboxStore.HasBeenProcessedAsync(123, Arg.Any<CancellationToken>())
            .Returns(true);
        _inboxStore.GetProcessedResultAsync(123, Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);
        
        var nextCalled = false;
        PipelineDelegate<string> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(CatgaResult<string>.Success("new"));
        };
        
        var result = await _behavior.HandleAsync(request, next);
        
        nextCalled.Should().BeFalse();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyProcessedButEmptyCachedResult_ShouldReturnSuccessWithDefault()
    {
        var request = new TestInboxRequest { MessageId = 123 };
        
        _inboxStore.HasBeenProcessedAsync(123, Arg.Any<CancellationToken>())
            .Returns(true);
        _inboxStore.GetProcessedResultAsync(123, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<byte>());
        
        var nextCalled = false;
        PipelineDelegate<string> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(CatgaResult<string>.Success("new"));
        };
        
        var result = await _behavior.HandleAsync(request, next);
        
        nextCalled.Should().BeFalse();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenDeserializationFails_ShouldReturnSuccessWithDefault()
    {
        var request = new TestInboxRequest { MessageId = 123 };
        var cachedBytes = new byte[] { 1, 2, 3 };
        
        _inboxStore.HasBeenProcessedAsync(123, Arg.Any<CancellationToken>())
            .Returns(true);
        _inboxStore.GetProcessedResultAsync(123, Arg.Any<CancellationToken>())
            .Returns(cachedBytes);
        _serializer.Deserialize<CatgaResult<string>>(cachedBytes)
            .Throws(new InvalidOperationException("Deserialization failed"));
        
        var nextCalled = false;
        PipelineDelegate<string> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(CatgaResult<string>.Success("new"));
        };
        
        var result = await _behavior.HandleAsync(request, next);
        
        nextCalled.Should().BeFalse();
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region HandleAsync Tests - Lock Failed

    [Fact]
    public async Task HandleAsync_WhenLockFails_ShouldReturnFailure()
    {
        var request = new TestInboxRequest { MessageId = 123 };
        
        _inboxStore.HasBeenProcessedAsync(123, Arg.Any<CancellationToken>())
            .Returns(false);
        _inboxStore.TryLockMessageAsync(123, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false);
        
        var nextCalled = false;
        PipelineDelegate<string> next = () =>
        {
            nextCalled = true;
            return ValueTask.FromResult(CatgaResult<string>.Success("new"));
        };
        
        var result = await _behavior.HandleAsync(request, next);
        
        nextCalled.Should().BeFalse();
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.LockFailed);
        result.Error.IsRetryable.Should().BeTrue();
    }

    #endregion

    #region HandleAsync Tests - Successful Processing

    [Fact]
    public async Task HandleAsync_WhenProcessingSucceeds_ShouldMarkAsProcessed()
    {
        var request = new TestInboxRequest { MessageId = 123, CorrelationId = new CorrelationId("corr-1") };
        var expectedResult = CatgaResult<string>.Success("result");
        var requestBytes = new byte[] { 1, 2, 3 };
        var resultBytes = new byte[] { 4, 5, 6 };
        
        _inboxStore.HasBeenProcessedAsync(123, Arg.Any<CancellationToken>())
            .Returns(false);
        _inboxStore.TryLockMessageAsync(123, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _serializer.Serialize(request).Returns(requestBytes);
        _serializer.Serialize(expectedResult).Returns(resultBytes);
        
        PipelineDelegate<string> next = () => ValueTask.FromResult(expectedResult);
        
        var result = await _behavior.HandleAsync(request, next);
        
        result.Should().Be(expectedResult);
        await _inboxStore.Received(1).MarkAsProcessedAsync(
            Arg.Is<InboxMessage>(m => 
                m.MessageId == 123 && 
                m.Payload == requestBytes && 
                m.ProcessingResult == resultBytes),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region HandleAsync Tests - Processing Error

    [Fact]
    public async Task HandleAsync_WhenProcessingThrows_ShouldReleaseLockAndReturnFailure()
    {
        var request = new TestInboxRequest { MessageId = 123 };
        var exception = new InvalidOperationException("Processing failed");
        
        _inboxStore.HasBeenProcessedAsync(123, Arg.Any<CancellationToken>())
            .Returns(false);
        _inboxStore.TryLockMessageAsync(123, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
        
        PipelineDelegate<string> next = () => throw exception;
        
        var result = await _behavior.HandleAsync(request, next);
        
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.PersistenceFailed);
        result.Error.IsRetryable.Should().BeTrue();
        await _inboxStore.Received(1).ReleaseLockAsync(123, Arg.Any<CancellationToken>());
    }

    #endregion

    #region HandleAsync Tests - Persistence Error

    [Fact]
    public async Task HandleAsync_WhenHasBeenProcessedThrows_ShouldReturnFailure()
    {
        var request = new TestInboxRequest { MessageId = 123 };
        var exception = new InvalidOperationException("Persistence error");
        
        _inboxStore.HasBeenProcessedAsync(123, Arg.Any<CancellationToken>())
            .Throws(exception);
        
        PipelineDelegate<string> next = () => ValueTask.FromResult(CatgaResult<string>.Success("result"));
        
        var result = await _behavior.HandleAsync(request, next);
        
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(ErrorCodes.PersistenceFailed);
    }

    #endregion

    #region InboxBehaviorOptions Tests

    [Fact]
    public void InboxBehaviorOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new InboxBehaviorOptions();
        
        options.LockDuration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void InboxBehaviorOptions_CustomValues_ShouldBeSet()
    {
        var options = new InboxBehaviorOptions
        {
            LockDuration = TimeSpan.FromMinutes(15)
        };
        
        options.LockDuration.Should().Be(TimeSpan.FromMinutes(15));
    }

    #endregion

    #region Test Helpers

    public record TestInboxRequest : IRequest<string>, IMessage
    {
        public long MessageId { get; init; }
        public CorrelationId? CorrelationId { get; init; }
    }

    #endregion
}
