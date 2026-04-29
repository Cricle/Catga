using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Flow.Dsl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.DependencyInjection;

public class CatgaLifetimeValidationTests
{
    [Fact]
    public void ValidateCatgaLifetimes_ShouldRejectSingletonHandlerDependingOnScopedMediator()
    {
        var services = new ServiceCollection();

        services.AddCatga();
        services.AddSingleton<IRequestHandler<ValidationCommand, string>, SingletonMediatorHandler>();

        var act = () => services.ValidateCatgaLifetimes();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SingletonMediatorHandler*ICatgaMediator*");
    }

    [Fact]
    public void ValidateCatgaLifetimes_ShouldRejectSingletonFlowExecutorDependingOnScopedMediator()
    {
        var services = new ServiceCollection();

        services.AddCatga();
        services.AddSingleton<IFlowExecutor, SingletonFlowExecutor>();

        var act = () => services.ValidateCatgaLifetimes();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SingletonFlowExecutor*ICatgaMediator*");
    }

    [Fact]
    public void ValidateCatgaLifetimes_ShouldAllowScopedHandlerDependingOnScopedMediator()
    {
        var services = new ServiceCollection();

        services.AddCatga();
        services.AddScoped<IRequestHandler<ValidationCommand, string>, ScopedMediatorHandler>();

        var act = () => services.ValidateCatgaLifetimes();

        act.Should().NotThrow();
    }

    [Fact]
    public void AddHostedServices_ShouldValidateLateHandlerRegistrations()
    {
        var services = new ServiceCollection();

        var builder = services.AddCatga();
        services.AddSingleton<IRequestHandler<ValidationCommand, string>, SingletonMediatorHandler>();

        var act = () => builder.AddHostedServices(options =>
        {
            options.EnableAutoRecovery = false;
            options.EnableTransportHosting = false;
            options.EnableOutboxProcessor = false;
        });

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SingletonMediatorHandler*ICatgaMediator*");
    }

    [Fact]
    public void UseAutoCompensation_ShouldRegisterScopedBehavior()
    {
        var services = new ServiceCollection();

        services.AddCatga().UseAutoCompensation();

        services.Should().Contain(descriptor =>
            descriptor.ServiceType.IsGenericTypeDefinition &&
            descriptor.ServiceType == typeof(Catga.Pipeline.IPipelineBehavior<,>) &&
            descriptor.ImplementationType != null &&
            descriptor.ImplementationType.Name.Contains("CompensationBehavior", StringComparison.Ordinal) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private sealed record ValidationCommand(long MessageId) : IRequest<string>;

    private sealed class SingletonMediatorHandler(ICatgaMediator mediator) : IRequestHandler<ValidationCommand, string>
    {
        private readonly ICatgaMediator _mediator = mediator;

        public ValueTask<CatgaResult<string>> HandleAsync(ValidationCommand request, CancellationToken cancellationToken = default)
        {
            _ = _mediator;
            return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success(nameof(SingletonMediatorHandler)));
        }
    }

    private sealed class ScopedMediatorHandler(ICatgaMediator mediator) : IRequestHandler<ValidationCommand, string>
    {
        private readonly ICatgaMediator _mediator = mediator;

        public ValueTask<CatgaResult<string>> HandleAsync(ValidationCommand request, CancellationToken cancellationToken = default)
        {
            _ = _mediator;
            return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success(nameof(ScopedMediatorHandler)));
        }
    }

    private sealed class SingletonFlowExecutor(ICatgaMediator mediator) : IFlowExecutor
    {
        private readonly ICatgaMediator _mediator = mediator;

        public Task<bool> CancelAsync(string flowId, CancellationToken cancellationToken = default)
        {
            _ = _mediator;
            return Task.FromResult(true);
        }

        public Task<DslFlowResult<TState>> ExecuteAsync<TFlow, TState>(TState initialState, CancellationToken cancellationToken = default)
            where TFlow : FlowConfig<TState>, new()
            where TState : class, IFlowState, new()
            => throw new NotSupportedException();

        public Task<FlowSnapshot<TState>?> GetSnapshotAsync<TState>(string flowId, CancellationToken cancellationToken = default)
            where TState : class, IFlowState, new()
            => throw new NotSupportedException();

        public Task<DslFlowResult<TState>> ResumeAsync<TFlow, TState>(string flowId, CancellationToken cancellationToken = default)
            where TFlow : FlowConfig<TState>, new()
            where TState : class, IFlowState, new()
            => throw new NotSupportedException();
    }
}
