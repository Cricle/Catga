using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Persistence.Redis.Locking;
using Catga.Persistence.Redis.Scheduling;
using Catga.Persistence.Redis.Stores;
using Catga.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Persistence.Redis.DependencyInjection;

/// <summary>
/// Extension methods for Redis distributed features.
/// </summary>
public static class RedisDistributedExtensions
{
    /// <summary>Add Redis distributed lock.</summary>
    public static CatgaServiceBuilder UseRedisDistributedLock(
        this CatgaServiceBuilder builder,
        Action<DistributedLockOptions>? configure = null)
    {
        if (configure != null)
            builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<IDistributedLock, RedisDistributedLock>();
        return builder;
    }

    /// <summary>Add Redis message scheduler.</summary>
    public static CatgaServiceBuilder UseRedisMessageScheduler(
        this CatgaServiceBuilder builder,
        Action<MessageSchedulerOptions>? configure = null)
    {
        if (configure != null)
            builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<IMessageScheduler, RedisMessageScheduler>();
        builder.Services.AddHostedService(sp => (RedisMessageScheduler)sp.GetRequiredService<IMessageScheduler>());
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
            .UseRedisMessageScheduler()
            .UseRedisSnapshotStore();
    }

    /// <summary>Add all Redis distributed features with options.</summary>
    public static CatgaServiceBuilder UseRedisDistributed(
        this CatgaServiceBuilder builder,
        Action<DistributedLockOptions>? lockOptions = null,
        Action<MessageSchedulerOptions>? schedulerOptions = null,
        Action<SnapshotOptions>? snapshotOptions = null)
    {
        return builder
            .UseRedisDistributedLock(lockOptions)
            .UseRedisMessageScheduler(schedulerOptions)
            .UseRedisSnapshotStore(snapshotOptions);
    }
}
