using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Pipeline;

/// <summary>
/// Coverage tests for pipeline behaviors
/// </summary>
public sealed partial class PipelineBehaviorCoverageTests
{
    [Fact]
    public async Task LoggingBehavior_ShouldLogRequestExecution()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<LoggedCommand, LoggedResponse>, LoggedHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new LoggedCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<LoggedCommand, LoggedResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidationBehavior_WithValidRequest_ShouldPass()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ValidatedCommand, ValidatedResponse>, ValidatedHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new ValidatedCommand { MessageId = MessageExtensions.NewMessageId(), Name = "Valid" };
        var result = await mediator.SendAsync<ValidatedCommand, ValidatedResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task MultipleBehaviors_ShouldExecuteInOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddSingleton(executionOrder);
        services.AddScoped<IRequestHandler<OrderedCommand, OrderedResponse>, OrderedHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(FirstOrderBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(SecondOrderBehavior<,>));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new OrderedCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<OrderedCommand, OrderedResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        executionOrder.Should().ContainInOrder("First-Before", "Second-Before", "Handler", "Second-After", "First-After");
    }

    [Fact]
    public async Task Behavior_ThrowsException_ShouldPropagateAsFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<FailBehaviorCommand, FailBehaviorResponse>, FailBehaviorHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ThrowingBehavior<,>));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new FailBehaviorCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<FailBehaviorCommand, FailBehaviorResponse>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Behavior_CanShortCircuit_WithoutCallingNext()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ShortCircuitCommand, ShortCircuitResponse>, ShortCircuitHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ShortCircuitBehavior<,>));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        ShortCircuitHandler.WasCalled = false;

        // Act
        var command = new ShortCircuitCommand { MessageId = MessageExtensions.NewMessageId(), ShouldShortCircuit = true };
        var result = await mediator.SendAsync<ShortCircuitCommand, ShortCircuitResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.FromBehavior.Should().BeTrue();
        ShortCircuitHandler.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Behavior_CanModifyResponse()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ModifyCommand, ModifyResponse>, ModifyHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ModifyingBehavior<,>));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new ModifyCommand { MessageId = MessageExtensions.NewMessageId(), Value = 10 };
        var result = await mediator.SendAsync<ModifyCommand, ModifyResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ModifiedValue.Should().Be(20); // Handler returns 10, behavior doubles it
    }

    [Fact]
    public async Task Behavior_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<CancelBehaviorCommand, CancelBehaviorResponse>, CancelBehaviorHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CancellationAwareBehavior<,>));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var command = new CancelBehaviorCommand { MessageId = MessageExtensions.NewMessageId() };

        try
        {
            var result = await mediator.SendAsync<CancelBehaviorCommand, CancelBehaviorResponse>(command, cts.Token);
            result.IsSuccess.Should().BeFalse();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    #region Test Types

    [MemoryPackable]
    private partial record LoggedCommand : IRequest<LoggedResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record LoggedResponse { }

    private sealed class LoggedHandler : IRequestHandler<LoggedCommand, LoggedResponse>
    {
        public ValueTask<CatgaResult<LoggedResponse>> HandleAsync(LoggedCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<LoggedResponse>>(CatgaResult<LoggedResponse>.Success(new LoggedResponse()));
        }
    }

    [MemoryPackable]
    private partial record ValidatedCommand : IRequest<ValidatedResponse>
    {
        public required long MessageId { get; init; }
        public string Name { get; init; } = "";
    }

    [MemoryPackable]
    private partial record ValidatedResponse { }

    private sealed class ValidatedHandler : IRequestHandler<ValidatedCommand, ValidatedResponse>
    {
        public ValueTask<CatgaResult<ValidatedResponse>> HandleAsync(ValidatedCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<ValidatedResponse>>(CatgaResult<ValidatedResponse>.Success(new ValidatedResponse()));
        }
    }

    [MemoryPackable]
    private partial record OrderedCommand : IRequest<OrderedResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record OrderedResponse { }

    private sealed class OrderedHandler : IRequestHandler<OrderedCommand, OrderedResponse>
    {
        private readonly List<string> _order;
        public OrderedHandler(List<string> order) => _order = order;

        public ValueTask<CatgaResult<OrderedResponse>> HandleAsync(OrderedCommand request, CancellationToken ct = default)
        {
            _order.Add("Handler");
            return new ValueTask<CatgaResult<OrderedResponse>>(CatgaResult<OrderedResponse>.Success(new OrderedResponse()));
        }
    }

    private sealed class FirstOrderBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly List<string> _order;
        public FirstOrderBehavior(List<string> order) => _order = order;

        public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            _order.Add("First-Before");
            var result = await next();
            _order.Add("First-After");
            return result;
        }
    }

    private sealed class SecondOrderBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly List<string> _order;
        public SecondOrderBehavior(List<string> order) => _order = order;

        public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            _order.Add("Second-Before");
            var result = await next();
            _order.Add("Second-After");
            return result;
        }
    }

    [MemoryPackable]
    private partial record FailBehaviorCommand : IRequest<FailBehaviorResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record FailBehaviorResponse { }

    private sealed class FailBehaviorHandler : IRequestHandler<FailBehaviorCommand, FailBehaviorResponse>
    {
        public ValueTask<CatgaResult<FailBehaviorResponse>> HandleAsync(FailBehaviorCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<FailBehaviorResponse>>(CatgaResult<FailBehaviorResponse>.Success(new FailBehaviorResponse()));
        }
    }

    private sealed class ThrowingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Behavior exception");
        }
    }

    [MemoryPackable]
    private partial record ShortCircuitCommand : IRequest<ShortCircuitResponse>
    {
        public required long MessageId { get; init; }
        public bool ShouldShortCircuit { get; init; }
    }

    [MemoryPackable]
    private partial record ShortCircuitResponse
    {
        public bool FromBehavior { get; init; }
    }

    private sealed class ShortCircuitHandler : IRequestHandler<ShortCircuitCommand, ShortCircuitResponse>
    {
        public static bool WasCalled;

        public ValueTask<CatgaResult<ShortCircuitResponse>> HandleAsync(ShortCircuitCommand request, CancellationToken ct = default)
        {
            WasCalled = true;
            return new ValueTask<CatgaResult<ShortCircuitResponse>>(CatgaResult<ShortCircuitResponse>.Success(new ShortCircuitResponse { FromBehavior = false }));
        }
    }

    private sealed class ShortCircuitBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            if (request is ShortCircuitCommand cmd && cmd.ShouldShortCircuit)
            {
                var response = (TResponse)(object)new ShortCircuitResponse { FromBehavior = true };
                return new ValueTask<CatgaResult<TResponse>>(CatgaResult<TResponse>.Success(response));
            }
            return next();
        }
    }

    [MemoryPackable]
    private partial record ModifyCommand : IRequest<ModifyResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record ModifyResponse
    {
        public int ModifiedValue { get; init; }
    }

    private sealed class ModifyHandler : IRequestHandler<ModifyCommand, ModifyResponse>
    {
        public ValueTask<CatgaResult<ModifyResponse>> HandleAsync(ModifyCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<ModifyResponse>>(CatgaResult<ModifyResponse>.Success(new ModifyResponse { ModifiedValue = request.Value }));
        }
    }

    private sealed class ModifyingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            var result = await next();
            if (result.IsSuccess && result.Value is ModifyResponse mr)
            {
                var modified = new ModifyResponse { ModifiedValue = mr.ModifiedValue * 2 };
                return CatgaResult<TResponse>.Success((TResponse)(object)modified);
            }
            return result;
        }
    }

    [MemoryPackable]
    private partial record CancelBehaviorCommand : IRequest<CancelBehaviorResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record CancelBehaviorResponse { }

    private sealed class CancelBehaviorHandler : IRequestHandler<CancelBehaviorCommand, CancelBehaviorResponse>
    {
        public ValueTask<CatgaResult<CancelBehaviorResponse>> HandleAsync(CancelBehaviorCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<CancelBehaviorResponse>>(CatgaResult<CancelBehaviorResponse>.Success(new CancelBehaviorResponse()));
        }
    }

    private sealed class CancellationAwareBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return next();
        }
    }

    #endregion
}
