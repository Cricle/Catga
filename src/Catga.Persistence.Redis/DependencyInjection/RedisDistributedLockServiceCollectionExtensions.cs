using Catga.DistributedLock;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.DependencyInjection;

/// <summary>
/// Extension methods for adding Redis distributed lock to the service collection
/// </summary>
public static class RedisDistributedLockServiceCollectionExtensions
{
    /// <summary>
    /// Add Redis distributed lock
    /// </summary>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services)
    {
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        return services;
    }

    /// <summary>
    /// Add Redis distributed lock with connection string
    /// </summary>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services,
        string connectionString)
    {
        var redis = ConnectionMultiplexer.Connect(connectionString);
        services.AddSingleton<IConnectionMultiplexer>(redis);
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        return services;
    }
}

