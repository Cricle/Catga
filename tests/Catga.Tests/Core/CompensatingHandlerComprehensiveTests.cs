using Catga.Abstractions;
using Catga.Core;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Core;

/// <summary>
/// Comprehensive tests for CompensatingHandler and Compensate helper
/// </summary>
public class CompensatingHandlerComprehensiveTests
{
    #region Test Types

    public record TestRequest(int Value) : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    public record TestCommand(string Name) : IRequest
    {
        public long MessageId { get; init; }
    }

    public record CompensationEvent(string Reason, int OriginalValue) : IEvent
    {
        public long MessageId { get; init; }
    }

    public record CommandCompensationEvent(string Reason) : IEvent
    {
        public long MessageId { get; init; }
    }

    #endregion

    #region CompensatingHandler<TRequest, TResponse, TCompensationEvent> Tests

    public class TestCompensatingHandler : CompensatingHandler<TestRequest, string, CompensationEvent>
    {
        private readonly bool _shouldThrow;
        private readonly string _result;

        public TestCompensatingHandler(ICatgaMediator mediator, bool shouldThrow = false, string result = "success")
            : base(mediator)
        {
            _shouldThrow = shouldThrow;
            _result = result;
        }

        protected override ValueTask<CatgaResult<string>> HandleCoreAsync(TestRequest request, CancellationToken ct)
        {
            if (_shouldThrow)
                throw new InvalidOperationException("Test exception");
            
            return ValueTask.FromResult(CatgaResult<string>.Success(_result));
        }

        protected override CompensationEvent? CreateCompensationEvent(TestRequest request, Exception ex)
        {
            return new CompensationEvent(ex.Message, request.Value);
        }
    }

    [Fact]
    public async Task CompensatingHandler_OnSuccess_ShouldNotPublishCompensation()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var handler = new TestCompensatingHandler(mediator, shouldThrow: false, result: "ok");
        var request = new TestRequest(42);

        var result = await handler.HandleAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
        await mediator.DidNotReceive().PublishAsync(Arg.Any<CompensationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompensatingHandler_OnException_ShouldPublishCompensation()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var handler = new TestCompensatingHandler(mediator, shouldThrow: true);
        var request = new TestRequest(42);

        var result = await handler.HandleAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Test exception");
        await mediator.Received(1).PublishAsync(
            Arg.Is<CompensationEvent>(e => e.Reason == "Test exception" && e.OriginalValue == 42),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region CompensatingHandler<TRequest, TCompensationEvent> Tests

    public class TestCommandCompensatingHandler : CompensatingHandler<TestCommand, CommandCompensationEvent>
    {
        private readonly bool _shouldThrow;

        public TestCommandCompensatingHandler(ICatgaMediator mediator, bool shouldThrow = false)
            : base(mediator)
        {
            _shouldThrow = shouldThrow;
        }

        protected override ValueTask<CatgaResult> HandleCoreAsync(TestCommand request, CancellationToken ct)
        {
            if (_shouldThrow)
                throw new InvalidOperationException("Command failed");
            
            return ValueTask.FromResult(CatgaResult.Success());
        }

        protected override CommandCompensationEvent? CreateCompensationEvent(TestCommand request, Exception ex)
        {
            return new CommandCompensationEvent(ex.Message);
        }
    }

    [Fact]
    public async Task CompensatingCommandHandler_OnSuccess_ShouldNotPublishCompensation()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var handler = new TestCommandCompensatingHandler(mediator, shouldThrow: false);
        var command = new TestCommand("test");

        var result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeTrue();
        await mediator.DidNotReceive().PublishAsync(Arg.Any<CommandCompensationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompensatingCommandHandler_OnException_ShouldPublishCompensation()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var handler = new TestCommandCompensatingHandler(mediator, shouldThrow: true);
        var command = new TestCommand("test");

        var result = await handler.HandleAsync(command);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Command failed");
        await mediator.Received(1).PublishAsync(
            Arg.Is<CommandCompensationEvent>(e => e.Reason == "Command failed"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Compensate Static Helper Tests

    [Fact]
    public async Task Compensate_WithCompensationAsync_OnSuccess_ShouldNotPublishCompensation()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var request = new TestRequest(42);

        var result = await Compensate.WithCompensationAsync<TestRequest, string, CompensationEvent>(
            mediator,
            request,
            (r, ct) => Task.FromResult(CatgaResult<string>.Success("ok")),
            (r, ex) => new CompensationEvent(ex.Message, r.Value));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
        await mediator.DidNotReceive().PublishAsync(Arg.Any<CompensationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Compensate_WithCompensationAsync_OnException_ShouldPublishCompensation()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var request = new TestRequest(42);

        var result = await Compensate.WithCompensationAsync<TestRequest, string, CompensationEvent>(
            mediator,
            request,
            (r, ct) => throw new InvalidOperationException("Handler failed"),
            (r, ex) => new CompensationEvent(ex.Message, r.Value));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Handler failed");
        await mediator.Received(1).PublishAsync(
            Arg.Is<CompensationEvent>(e => e.Reason == "Handler failed" && e.OriginalValue == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Compensate_WithCompensationAsyncNoResponse_OnSuccess_ShouldNotPublishCompensation()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var command = new TestCommand("test");

        var result = await Compensate.WithCompensationAsync<TestCommand, CommandCompensationEvent>(
            mediator,
            command,
            (r, ct) => Task.FromResult(CatgaResult.Success()),
            (r, ex) => new CommandCompensationEvent(ex.Message));

        result.IsSuccess.Should().BeTrue();
        await mediator.DidNotReceive().PublishAsync(Arg.Any<CommandCompensationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Compensate_WithCompensationAsyncNoResponse_OnException_ShouldPublishCompensation()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var command = new TestCommand("test");

        var result = await Compensate.WithCompensationAsync<TestCommand, CommandCompensationEvent>(
            mediator,
            command,
            (r, ct) => throw new InvalidOperationException("Command handler failed"),
            (r, ex) => new CommandCompensationEvent(ex.Message));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Command handler failed");
        await mediator.Received(1).PublishAsync(
            Arg.Is<CommandCompensationEvent>(e => e.Reason == "Command handler failed"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Null Compensation Event Tests

    public class NullCompensationHandler : CompensatingHandler<TestRequest, string, CompensationEvent>
    {
        public NullCompensationHandler(ICatgaMediator mediator) : base(mediator) { }

        protected override ValueTask<CatgaResult<string>> HandleCoreAsync(TestRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException("Test");
        }

        protected override CompensationEvent? CreateCompensationEvent(TestRequest request, Exception ex)
        {
            return null; // Return null to skip compensation
        }
    }

    [Fact]
    public async Task CompensatingHandler_WhenCompensationEventIsNull_ShouldNotPublish()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var handler = new NullCompensationHandler(mediator);
        var request = new TestRequest(42);

        var result = await handler.HandleAsync(request);

        result.IsSuccess.Should().BeFalse();
        await mediator.DidNotReceive().PublishAsync(Arg.Any<CompensationEvent>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task Compensate_WithCancellation_ShouldPassCancellationToken()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var request = new TestRequest(42);
        var cts = new CancellationTokenSource();
        var tokenReceived = false;

        var result = await Compensate.WithCompensationAsync<TestRequest, string, CompensationEvent>(
            mediator,
            request,
            (r, ct) =>
            {
                tokenReceived = ct == cts.Token;
                return Task.FromResult(CatgaResult<string>.Success("ok"));
            },
            (r, ex) => new CompensationEvent(ex.Message, r.Value),
            cts.Token);

        tokenReceived.Should().BeTrue();
    }

    #endregion
}
