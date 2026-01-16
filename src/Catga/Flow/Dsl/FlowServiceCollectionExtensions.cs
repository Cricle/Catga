using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Extension methods for registering Flow DSL services.
/// </summary>
public static class FlowServiceCollectionExtensions
{
    /// <summary>
    /// Add Flow DSL support to the service collection.
    /// Requires IDslFlowStore to be registered separately (e.g., via UseInMemory()).
    /// </summary>
    public static IServiceCollection AddFlowDsl(this IServiceCollection services)
    {
        // Register flow executor
        services.TryAddSingleton<IFlowExecutor, FlowExecutorService>();

        return services;
    }

    /// <summary>
    /// Add Flow DSL support with a custom store.
    /// </summary>
    public static IServiceCollection AddFlowDsl<TStore>(this IServiceCollection services)
        where TStore : class, IDslFlowStore
    {
        services.TryAddSingleton<IDslFlowStore, TStore>();
        services.TryAddSingleton<IFlowExecutor, FlowExecutorService>();

        return services;
    }
}
