using Catga.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Persistence.Redis.DependencyInjection;

/// <summary>
/// Extension methods for adding Redis distributed cache to the service collection (serializer-agnostic)
/// </summary>
public static class RedisDistributedCacheServiceCollectionExtensions
{
    /// <summary>
    /// Add Redis distributed cache (requires IMessageSerializer to be registered separately)
    /// </summary>
    public static IServiceCollection AddRedisDistributedCache(
        this IServiceCollection services)
    {
        services.AddSingleton<IDistributedCache, RedisDistributedCache>();
        return services;
    }
}

