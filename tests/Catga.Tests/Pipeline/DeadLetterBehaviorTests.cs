using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.Exceptions;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Pipeline;

public class DeadLetterBehaviorTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDeadLetterQueue _deadLetterQueue;
    private readonly DeadLetterBehavior<TestRequest, TestResponse> _behavior;

    public DeadLetterBehaviorTests()
    {
        _serviceProvider = Substitute.For<IServiceProvider>();
        _deadLetterQueue = Substitute.For<IDeadLetterQueue>();
        _serviceProvider.GetService(typeof(IDeadLetterQueue)).Returns(_deadLetterQueue);
        _behavior = new DeadLetterBehavior<TestRequest, TestResponse>(_serviceProvider);
    }

    [Fact]
    public void Constructor_WithNullServiceProvider_ShouldThrowArgumentNullException()
    {
        var act = () => new DeadLetterBehavior<TestRequest, TestResponse>(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public async Task HandleAsync_WhenResultSucceeds_ShouldNotWriteDeadLetter()
    {
        var request = new TestRequest { MessageId = 11, Data = "ok" };
        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(new TestResponse { Result = "done" }));

        var result = await _behavior.HandleAsync(request, next);

        result.IsSuccess.Should().BeTrue();
        await _deadLetterQueue.DidNotReceive().SendAsync(
            Arg.Any<TestRequest>(),
            Arg.Any<Exception>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenResultFailsWithException_ShouldWriteDeadLetterUsingOriginalException()
    {
        var request = new TestRequest { MessageId = 12, Data = "fail" };
        var exception = new CatgaException("boom", ErrorCodes.HandlerFailed);
        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Failure("boom", exception));

        var result = await _behavior.HandleAsync(request, next);

        result.IsSuccess.Should().BeFalse();
        result.Exception.Should().BeSameAs(exception);
        await _deadLetterQueue.Received(1).SendAsync(
            request,
            exception,
            0,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenResultFailsWithoutException_ShouldWriteGeneratedCatgaException()
    {
        var request = new TestRequest { MessageId = 13, Data = "fail" };
        Exception? capturedException = null;
        _deadLetterQueue.SendAsync(Arg.Any<TestRequest>(), Arg.Any<Exception>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo => capturedException = callInfo.ArgAt<Exception>(1));
        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Failure(new ErrorInfo
            {
                Code = ErrorCodes.ValidationFailed,
                Message = "validation failed",
                IsRetryable = false
            }));

        var result = await _behavior.HandleAsync(request, next);

        result.IsSuccess.Should().BeFalse();
        await _deadLetterQueue.Received(1).SendAsync(
            request,
            Arg.Any<Exception>(),
            0,
            Arg.Any<CancellationToken>());
        capturedException.Should().BeOfType<CatgaException>();
        ((CatgaException)capturedException!).Message.Should().Be("validation failed");
        ((CatgaException)capturedException!).ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task HandleAsync_WhenDeadLetterWriteFails_ShouldPreserveOriginalResult()
    {
        var request = new TestRequest { MessageId = 14, Data = "fail" };
        var failure = CatgaResult<TestResponse>.Failure("original failure");
        _deadLetterQueue.SendAsync(Arg.Any<TestRequest>(), Arg.Any<Exception>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("dlq down")));
        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(failure);

        var result = await _behavior.HandleAsync(request, next);

        result.Should().Be(failure);
    }

    [Fact]
    public async Task HandleAsync_WhenNextThrows_ShouldWriteDeadLetterAndRethrow()
    {
        var request = new TestRequest { MessageId = 15, Data = "throw" };
        var exception = new InvalidOperationException("pipeline exploded");
        PipelineDelegate<TestResponse> next = () => ValueTask.FromException<CatgaResult<TestResponse>>(exception);

        var act = () => _behavior.HandleAsync(request, next).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("pipeline exploded");

        await _deadLetterQueue.Received(1).SendAsync(
            request,
            exception,
            0,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDeadLetterQueueIsMissing_ShouldSkipDeadLetterWrite()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var behavior = new DeadLetterBehavior<TestRequest, TestResponse>(services);
        var request = new TestRequest { MessageId = 16, Data = "missing" };
        var failure = CatgaResult<TestResponse>.Failure("missing dlq");
        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(failure);

        var result = await behavior.HandleAsync(request, next);

        result.Should().Be(failure);
    }

    public sealed class TestRequest : IRequest<TestResponse>
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public sealed class TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }
}

public class DeadLetterBehaviorWithoutResponseTests
{
    [Fact]
    public async Task HandleAsync_WhenRequestWithoutResponseFails_ShouldWriteDeadLetter()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var deadLetterQueue = Substitute.For<IDeadLetterQueue>();
        serviceProvider.GetService(typeof(IDeadLetterQueue)).Returns(deadLetterQueue);
        var behavior = new DeadLetterBehavior<TestCommand>(serviceProvider);
        var request = new TestCommand { MessageId = 21, Data = "fail" };

        PipelineDelegate next = () => ValueTask.FromResult(CatgaResult.Failure("command failed"));

        var result = await behavior.HandleAsync(request, next);

        result.IsSuccess.Should().BeFalse();
        await deadLetterQueue.Received(1).SendAsync(
            request,
            Arg.Any<Exception>(),
            0,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mediator_SendAsyncWithoutResponse_ShouldExecutePipelineBehavior()
    {
        var services = new ServiceCollection();
        var deadLetterQueue = Substitute.For<IDeadLetterQueue>();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();
        services.AddSingleton<IRequestHandler<TestCommand>, FailingCommandHandler>();
        services.AddSingleton(typeof(IPipelineBehavior<>), typeof(DeadLetterBehavior<>));
        services.AddSingleton(deadLetterQueue);

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync(new TestCommand { MessageId = 22, Data = "through mediator" });

        result.IsSuccess.Should().BeFalse();
        await deadLetterQueue.Received(1).SendAsync(
            Arg.Is<TestCommand>(x => x.MessageId == 22),
            Arg.Any<Exception>(),
            0,
            Arg.Any<CancellationToken>());
    }

    public sealed class TestCommand : IRequest
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    private sealed class FailingCommandHandler : IRequestHandler<TestCommand>
    {
        public ValueTask<CatgaResult> HandleAsync(TestCommand request, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult.Failure("command failed"));
    }
}
