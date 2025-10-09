using Catga.Pipeline.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Caching;

/// <summary>
/// Extension methods for adding distributed cache to the service collection
/// </summary>
public static class DistributedCacheServiceCollectionExtensions
{
    /// <summary>
    /// Add caching behavior to the pipeline
    /// </summary>
    public static IServiceCollection AddCachingBehavior(
        this IServiceCollection services)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        return services;
    }
}

