using Catga.Inbox;
using Catga.Locking;
using Catga.Outbox;
using Catga.Persistence.Redis.Persistence;
using Catga.Persistence.Redis;
using Catga.Persistence.Redis.Locking;
using Catga.Persistence.Redis.RateLimiting;
using Catga.Persistence.Redis.Flow;
using Catga.Persistence.Redis.Stores;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Catga.Abstractions;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Catga.Idempotency;

namespace Catga.DependencyInjection;

/// <summary>
/// Redis 持久化存储服务注册扩展 (序列化器无关，需用户显式注册 IMessageSerializer)
/// </summary>
public static class RedisPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Redis Outbox 持久化存储 (requires IMessageSerializer to be registered separately)
    /// </summary>
    public static IServiceCollection AddRedisOutboxPersistence(
        this IServiceCollection services,
        Action<RedisOutboxOptions>? configure = null)
    {
        var options = new RedisOutboxOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IOutboxStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var logger = sp.GetRequiredService<ILogger<RedisOutboxPersistence>>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new RedisOutboxPersistence(redis, serializer, logger, provider, options);
        });

        return services;
    }

    /// <summary>
    /// 添加 Redis Inbox 持久化存储 (requires IMessageSerializer to be registered separately)
    /// </summary>
    public static IServiceCollection AddRedisInboxPersistence(
        this IServiceCollection services,
        Action<RedisInboxOptions>? configure = null)
    {
        var options = new RedisInboxOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IInboxStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var logger = sp.GetRequiredService<ILogger<RedisInboxPersistence>>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new RedisInboxPersistence(redis, serializer, logger, provider, options);
        });

        return services;
    }

    /// <summary>
    /// 添加完整的 Redis 持久化支持（Outbox + Inbox）
    /// </summary>
    public static IServiceCollection AddRedisPersistence(
        this IServiceCollection services,
        Action<RedisOutboxOptions>? configureOutbox = null,
        Action<RedisInboxOptions>? configureInbox = null)
    {
        services.AddRedisOutboxPersistence(configureOutbox);
        services.AddRedisInboxPersistence(configureInbox);
        services.AddRedisIdempotencyStore();

        return services;
    }

    /// <summary>
    /// Add Redis persistence with connection string
    /// </summary>
    public static IServiceCollection AddRedisPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(connectionString));
        return services.AddRedisPersistence();
    }

    /// <summary>
    /// 添加 Redis 幂等性存储 (requires IMessageSerializer to be registered separately)
    /// </summary>
    public static IServiceCollection AddRedisIdempotencyStore(
        this IServiceCollection services,
        Action<RedisIdempotencyOptions>? configure = null)
    {
        var options = new RedisIdempotencyOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IIdempotencyStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var logger = sp.GetRequiredService<ILogger<RedisIdempotencyStore>>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new RedisIdempotencyStore(redis, serializer, logger, provider, options);
        });

        return services;
    }

    /// <summary>
    /// 添加 Redis 分布式限流器
    /// </summary>
    public static IServiceCollection AddRedisRateLimiter(
        this IServiceCollection services,
        Action<DistributedRateLimiterOptions>? configure = null)
    {
        services.Configure<DistributedRateLimiterOptions>(options =>
        {
            configure?.Invoke(options);
        });

        services.TryAddSingleton<IDistributedRateLimiter, RedisRateLimiter>();

        return services;
    }

    /// <summary>
    /// Add Redis distributed lock provider for [DistributedLock] attribute support.
    /// </summary>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services,
        Action<DistributedLockOptions>? configure = null)
    {
        var options = new DistributedLockOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(Options.Create(options));
        services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();

        return services;
    }

    /// <summary>
    /// Add Redis DSL flow store for distributed flow execution.
    /// </summary>
    public static IServiceCollection AddRedisDslFlowStore(
        this IServiceCollection services,
        string prefix = "dslflow:")
    {
        services.TryAddSingleton<IDslFlowStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            return new RedisDslFlowStore(redis, serializer, prefix);
        });

        return services;
    }

    /// <summary>
    /// Add Redis flow store for saga/flow state management.
    /// </summary>
    public static IServiceCollection AddRedisFlowStore(
        this IServiceCollection services,
        string prefix = "flow:")
    {
        services.TryAddSingleton<IFlowStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisFlowStore(redis, prefix);
        });

        return services;
    }

    /// <summary>
    /// Add Redis snapshot store for event sourcing.
    /// </summary>
    public static IServiceCollection AddRedisSnapshotStore(
        this IServiceCollection services,
        Action<SnapshotOptions>? configure = null)
    {
        if (configure != null)
            services.Configure(configure);
        else
            services.TryAddSingleton(Options.Create(new SnapshotOptions()));

        services.TryAddSingleton<ISnapshotStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var options = sp.GetRequiredService<IOptions<SnapshotOptions>>();
            var logger = sp.GetRequiredService<ILogger<RedisSnapshotStore>>();
            return new RedisSnapshotStore(redis, serializer, options, logger);
        });

        return services;
    }
}
