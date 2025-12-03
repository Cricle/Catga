using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Pipeline;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// End-to-end tests for pipeline behaviors and mediator flow
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class PipelineE2ETests
{
    [Fact]
    public async Task Pipeline_MultipleBehaviors_ExecuteInCorrectOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<OrderCommand, OrderResult>, OrderHandler>();
        services.AddSingleton(executionOrder);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(FirstBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(SecondBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ThirdBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new OrderCommand { MessageId = MessageExtensions.NewMessageId(), Data = "test" };
        var result = await mediator.SendAsync<OrderCommand, OrderResult>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        executionOrder.Should().ContainInOrder("First-Before", "Second-Before", "Third-Before", "Handler", "Third-After", "Second-After", "First-After");
    }

    [Fact]
    public async Task Pipeline_BehaviorThrows_ShouldPropagateException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<FailingCommand, FailingResult>, FailingHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ThrowingBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new FailingCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<FailingCommand, FailingResult>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task Pipeline_WithValidation_ShouldRejectInvalidRequest()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ValidatedCommand, ValidatedResult>, ValidatedHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Invalid command (empty name)
        var invalidCommand = new ValidatedCommand { MessageId = MessageExtensions.NewMessageId(), Name = "" };
        var result = await mediator.SendAsync<ValidatedCommand, ValidatedResult>(invalidCommand);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("validation");
    }

    [Fact]
    public async Task Pipeline_WithValidation_ShouldAcceptValidRequest()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ValidatedCommand, ValidatedResult>, ValidatedHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Valid command
        var validCommand = new ValidatedCommand { MessageId = MessageExtensions.NewMessageId(), Name = "ValidName" };
        var result = await mediator.SendAsync<ValidatedCommand, ValidatedResult>(validCommand);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Greeting.Should().Contain("ValidName");
    }

    [Fact]
    public async Task Pipeline_WithRetry_ShouldRetryOnTransientFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var attemptTracker = new AttemptTracker();
        services.AddSingleton(attemptTracker);
        services.AddScoped<IRequestHandler<RetryableCommand, RetryableResult>, RetryableHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(SimpleRetryBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new RetryableCommand { MessageId = MessageExtensions.NewMessageId(), FailUntilAttempt = 3 };
        var result = await mediator.SendAsync<RetryableCommand, RetryableResult>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        attemptTracker.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task Pipeline_CancellationToken_ShouldBePropagated()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<CancellableCommand, CancellableResult>, CancellableHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var command = new CancellableCommand { MessageId = MessageExtensions.NewMessageId() };

        // The mediator may either throw OperationCanceledException or return a failure result
        try
        {
            var result = await mediator.SendAsync<CancellableCommand, CancellableResult>(command, cts.Token);
            // If no exception, the result should indicate failure
            result.IsSuccess.Should().BeFalse();
        }
        catch (OperationCanceledException)
        {
            // This is also acceptable behavior
        }
    }

    #region Commands, Results, and Handlers

    [MemoryPackable]
    private partial record OrderCommand : IRequest<OrderResult>
    {
        public required long MessageId { get; init; }
        public required string Data { get; init; }
    }

    [MemoryPackable]
    private partial record OrderResult
    {
        public required string Result { get; init; }
    }

    private sealed class OrderHandler : IRequestHandler<OrderCommand, OrderResult>
    {
        private readonly List<string> _executionOrder;
        public OrderHandler(List<string> executionOrder) => _executionOrder = executionOrder;

        public Task<CatgaResult<OrderResult>> HandleAsync(OrderCommand request, CancellationToken ct = default)
        {
            _executionOrder.Add("Handler");
            return Task.FromResult(CatgaResult<OrderResult>.Success(new OrderResult { Result = $"handled-{request.Data}" }));
        }
    }

    private sealed class FirstBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly List<string> _executionOrder;
        public FirstBehavior(List<string> executionOrder) => _executionOrder = executionOrder;

        public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            _executionOrder.Add("First-Before");
            var result = await next();
            _executionOrder.Add("First-After");
            return result;
        }
    }

    private sealed class SecondBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly List<string> _executionOrder;
        public SecondBehavior(List<string> executionOrder) => _executionOrder = executionOrder;

        public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            _executionOrder.Add("Second-Before");
            var result = await next();
            _executionOrder.Add("Second-After");
            return result;
        }
    }

    private sealed class ThirdBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly List<string> _executionOrder;
        public ThirdBehavior(List<string> executionOrder) => _executionOrder = executionOrder;

        public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            _executionOrder.Add("Third-Before");
            var result = await next();
            _executionOrder.Add("Third-After");
            return result;
        }
    }

    [MemoryPackable]
    private partial record FailingCommand : IRequest<FailingResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record FailingResult { }

    private sealed class FailingHandler : IRequestHandler<FailingCommand, FailingResult>
    {
        public Task<CatgaResult<FailingResult>> HandleAsync(FailingCommand request, CancellationToken ct = default)
        {
            return Task.FromResult(CatgaResult<FailingResult>.Success(new FailingResult()));
        }
    }

    private sealed class ThrowingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Behavior threw an exception");
        }
    }

    [MemoryPackable]
    private partial record ValidatedCommand : IRequest<ValidatedResult>
    {
        public required long MessageId { get; init; }
        public required string Name { get; init; }
    }

    [MemoryPackable]
    private partial record ValidatedResult
    {
        public required string Greeting { get; init; }
    }

    private sealed class ValidatedHandler : IRequestHandler<ValidatedCommand, ValidatedResult>
    {
        public Task<CatgaResult<ValidatedResult>> HandleAsync(ValidatedCommand request, CancellationToken ct = default)
        {
            return Task.FromResult(CatgaResult<ValidatedResult>.Success(new ValidatedResult { Greeting = $"Hello, {request.Name}!" }));
        }
    }

    private sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            if (request is ValidatedCommand cmd && string.IsNullOrWhiteSpace(cmd.Name))
            {
                return ValueTask.FromResult(CatgaResult<TResponse>.Failure("Name validation failed"));
            }
            return next();
        }
    }

    [MemoryPackable]
    private partial record RetryableCommand : IRequest<RetryableResult>
    {
        public required long MessageId { get; init; }
        public int FailUntilAttempt { get; init; } = 1;
    }

    [MemoryPackable]
    private partial record RetryableResult
    {
        public required int AttemptNumber { get; init; }
    }

    private sealed class AttemptTracker
    {
        public int Attempts;
    }

    private sealed class RetryableHandler : IRequestHandler<RetryableCommand, RetryableResult>
    {
        private readonly AttemptTracker _tracker;
        public RetryableHandler(AttemptTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<RetryableResult>> HandleAsync(RetryableCommand request, CancellationToken ct = default)
        {
            var attempt = Interlocked.Increment(ref _tracker.Attempts);
            if (attempt < request.FailUntilAttempt)
            {
                throw new InvalidOperationException($"Transient failure on attempt {attempt}");
            }
            return Task.FromResult(CatgaResult<RetryableResult>.Success(new RetryableResult { AttemptNumber = attempt }));
        }
    }

    private sealed class SimpleRetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            const int maxRetries = 5;
            for (var i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await next();
                }
                catch (InvalidOperationException) when (i < maxRetries - 1)
                {
                    await Task.Delay(10, ct);
                }
            }
            return await next();
        }
    }

    [MemoryPackable]
    private partial record CancellableCommand : IRequest<CancellableResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record CancellableResult { }

    private sealed class CancellableHandler : IRequestHandler<CancellableCommand, CancellableResult>
    {
        public Task<CatgaResult<CancellableResult>> HandleAsync(CancellableCommand request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CatgaResult<CancellableResult>.Success(new CancellableResult()));
        }
    }

    #endregion
}
