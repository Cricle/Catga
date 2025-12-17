using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Flow.HotReload;

/// <summary>
/// DI extension methods for Flow Hot Reload.
/// </summary>
public static class FlowHotReloadExtensions
{
    /// <summary>
    /// Adds Flow Hot Reload services to the service collection.
    /// </summary>
    public static IServiceCollection AddFlowHotReload(this IServiceCollection services)
    {
        services.TryAddSingleton<IFlowRegistry, FlowRegistry>();
        services.TryAddSingleton<IFlowVersionManager, FlowVersionManager>();
        services.TryAddSingleton<IFlowReloader, FlowReloader>();

        return services;
    }

    /// <summary>
    /// Adds Flow Hot Reload services with custom implementations.
    /// </summary>
    public static IServiceCollection AddFlowHotReload<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRegistry,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TVersionManager,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TReloader>(
        this IServiceCollection services)
        where TRegistry : class, IFlowRegistry
        where TVersionManager : class, IFlowVersionManager
        where TReloader : class, IFlowReloader
    {
        services.TryAddSingleton<IFlowRegistry, TRegistry>();
        services.TryAddSingleton<IFlowVersionManager, TVersionManager>();
        services.TryAddSingleton<IFlowReloader, TReloader>();

        return services;
    }

    /// <summary>
    /// Adds a typed flow registry for a specific state type.
    /// </summary>
    public static IServiceCollection AddTypedFlowRegistry<TState>(this IServiceCollection services)
        where TState : class, Dsl.IFlowState
    {
        services.TryAddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<IFlowRegistry>();
            return new TypedFlowRegistry<TState>(registry);
        });

        return services;
    }

    /// <summary>
    /// Registers a flow configuration with the registry.
    /// </summary>
    public static IServiceCollection RegisterFlow<TState>(
        this IServiceCollection services,
        string flowName,
        Func<IServiceProvider, Dsl.IFlowBuilder<TState>> builderFactory)
        where TState : class, Dsl.IFlowState
    {
        services.AddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<IFlowRegistry>();
            var builder = builderFactory(sp);
            registry.Register(flowName, builder);
            return new FlowRegistration(flowName);
        });

        return services;
    }
}

/// <summary>
/// Marker record for flow registration.
/// </summary>
public record FlowRegistration(string FlowName);
