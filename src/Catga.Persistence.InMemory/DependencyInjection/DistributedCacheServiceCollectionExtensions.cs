using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Caching;

/// <summary>Distributed cache service collection extensions</summary>
public static class DistributedCacheServiceCollectionExtensions
{
    public static IServiceCollection AddCachingBehavior(this IServiceCollection services)
    {
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        return services;
    }
}

