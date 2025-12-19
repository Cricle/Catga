using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Persistence.Redis.Stores;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.DependencyInjection;

/// <summary>
/// Extension methods for Redis distributed features.
/// </summary>
public static class RedisDistributedExtensions
{
    /// <summary>Add Redis distributed lock provider using DistributedLock.Redis.</summary>
    public static CatgaServiceBuilder UseRedisDistributedLock(this CatgaServiceBuilder builder)
    {
        builder.Services.TryAddSingleton<IDistributedLockProvider>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisDistributedSynchronizationProvider(redis.GetDatabase());
        });
        return builder;
    }

    /// <summary>Add Redis snapshot store.</summary>
    public static CatgaServiceBuilder UseRedisSnapshotStore(
        this CatgaServiceBuilder builder,
        Action<SnapshotOptions>? configure = null)
    {
        if (configure != null)
            builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<ISnapshotStore, RedisSnapshotStore>();
        return builder;
    }

    /// <summary>Add all Redis distributed features.</summary>
    public static CatgaServiceBuilder UseRedisDistributed(this CatgaServiceBuilder builder)
    {
        return builder
            .UseRedisDistributedLock()
            .UseRedisSnapshotStore();
    }

    /// <summary>Add all Redis distributed features with options.</summary>
    public static CatgaServiceBuilder UseRedisDistributed(
        this CatgaServiceBuilder builder,
        Action<SnapshotOptions>? snapshotOptions = null)
    {
        return builder
            .UseRedisDistributedLock()
            .UseRedisSnapshotStore(snapshotOptions);
    }
}
