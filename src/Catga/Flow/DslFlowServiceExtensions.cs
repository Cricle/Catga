using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Extension methods for registering DSL Flow services.
/// </summary>
public static class DslFlowServiceExtensions
{
    /// <summary>
    /// Adds DSL Flow services with custom store.
    /// </summary>
    public static IServiceCollection AddDslFlow<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(this IServiceCollection services)
        where TStore : class, IDslFlowStore
    {
        services.TryAddSingleton<IDslFlowStore, TStore>();
        services.TryAddSingleton<FlowResumeHandler>();
        services.TryAddSingleton<IEventHandler<FlowCompletedEvent>>(sp => sp.GetRequiredService<FlowResumeHandler>());
        return services;
    }

    /// <summary>
    /// Adds the flow timeout service.
    /// </summary>
    public static IServiceCollection AddFlowTimeoutService(this IServiceCollection services, TimeSpan? checkInterval = null)
    {
        services.AddSingleton(sp => new FlowTimeoutService(
            sp.GetRequiredService<IDslFlowStore>(),
            checkInterval));
        return services;
    }

    /// <summary>
    /// Registers a flow configuration and its executor.
    /// </summary>
    public static IServiceCollection AddFlow<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfig>(this IServiceCollection services)
        where TState : class, IFlowState, new()
        where TConfig : FlowConfig<TState>, new()
    {
        services.TryAddSingleton<TConfig>();
        services.TryAddScoped<IFlow<TState>, DslFlowExecutor<TState, TConfig>>();
        return services;
    }

    /// <summary>
    /// Registers a flow configuration with a factory.
    /// </summary>
    public static IServiceCollection AddFlow<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, TConfig>(
        this IServiceCollection services,
        Func<IServiceProvider, TConfig> configFactory)
        where TState : class, IFlowState, new()
        where TConfig : FlowConfig<TState>
    {
        services.TryAddSingleton(configFactory);
        services.TryAddScoped<IFlow<TState>, DslFlowExecutor<TState, TConfig>>();
        return services;
    }
}
