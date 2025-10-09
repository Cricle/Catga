using Catga.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Persistence.Redis.DependencyInjection;

/// <summary>
/// Extension methods for adding Redis distributed cache to the service collection
/// </summary>
public static class RedisDistributedCacheServiceCollectionExtensions
{
    /// <summary>
    /// Add Redis distributed cache
    /// </summary>
    public static IServiceCollection AddRedisDistributedCache(
        this IServiceCollection services)
    {
        services.AddSingleton<IDistributedCache, RedisDistributedCache>();
        return services;
    }
}

