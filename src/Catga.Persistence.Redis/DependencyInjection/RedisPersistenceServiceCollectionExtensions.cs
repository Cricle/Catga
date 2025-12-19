using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.Redis.Persistence;
using Catga.Persistence.Redis;
using Catga.Persistence.Redis.Flow;
using Catga.Persistence.Redis.Stores;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.EventSourcing;
using Catga.DeadLetter;
using Catga.Core;
using Medallion.Threading;
using Medallion.Threading.Redis;
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
    /// Ensure all required Redis dependencies are registered.
    /// </summary>
    private static void EnsureRedisConnectionRegistered(IServiceCollection services)
    {
        // Check if IConnectionMultiplexer is already registered
        if (!services.Any(sd => sd.ServiceType == typeof(IConnectionMultiplexer)))
        {
            throw new InvalidOperationException(
                "IConnectionMultiplexer is not registered. " +
                "Call AddRedisPersistence(connectionString) or register IConnectionMultiplexer manually.");
        }
    }
    /// <summary>
    /// 添加 Redis Outbox 持久化存储 (requires IMessageSerializer to be registered separately)
    /// </summary>
    public static IServiceCollection AddRedisOutboxPersistence(
        this IServiceCollection services,
        Action<RedisPersistenceOptions>? configure = null)
    {
        EnsureRedisConnectionRegistered(services);
        var options = new RedisPersistenceOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(Options.Create(options));
        services.TryAddSingleton<IOutboxStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var logger = sp.GetRequiredService<ILogger<RedisOutboxPersistence>>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            var opts = sp.GetRequiredService<IOptions<RedisPersistenceOptions>>();
            return new RedisOutboxPersistence(redis, serializer, logger, provider, opts);
        });

        return services;
    }

    /// <summary>
    /// 添加 Redis Inbox 持久化存储 (requires IMessageSerializer to be registered separately)
    /// </summary>
    public static IServiceCollection AddRedisInboxPersistence(
        this IServiceCollection services,
        Action<RedisPersistenceOptions>? configure = null)
    {
        EnsureRedisConnectionRegistered(services);
        var options = new RedisPersistenceOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(Options.Create(options));
        services.TryAddSingleton<IInboxStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var logger = sp.GetRequiredService<ILogger<RedisInboxPersistence>>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            var opts = sp.GetRequiredService<IOptions<RedisPersistenceOptions>>();
            return new RedisInboxPersistence(redis, serializer, logger, provider, opts);
        });

        return services;
    }

    /// <summary>
    /// 添加完整的 Redis 持久化支持（Outbox + Inbox）
    /// </summary>
    public static IServiceCollection AddRedisPersistence(
        this IServiceCollection services,
        Action<RedisPersistenceOptions>? configure = null)
    {
        services.AddRedisOutboxPersistence(configure);
        services.AddRedisInboxPersistence(configure);
        services.AddRedisIdempotencyStore(configure);

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
        Action<RedisPersistenceOptions>? configure = null)
    {
        EnsureRedisConnectionRegistered(services);
        var options = new RedisPersistenceOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(Options.Create(options));
        services.TryAddSingleton<IIdempotencyStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var logger = sp.GetRequiredService<ILogger<RedisIdempotencyStore>>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            var opts = sp.GetRequiredService<IOptions<RedisPersistenceOptions>>();
            return new RedisIdempotencyStore(redis, serializer, logger, provider, opts);
        });

        return services;
    }

    /// <summary>
    /// Add Redis distributed lock provider for [DistributedLock] attribute support.
    /// Uses DistributedLock.Redis library.
    /// </summary>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services)
    {
        EnsureRedisConnectionRegistered(services);
        services.TryAddSingleton<IDistributedLockProvider>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisDistributedSynchronizationProvider(redis.GetDatabase());
        });

        return services;
    }

    /// <summary>
    /// Add Redis DSL flow store for distributed flow execution.
    /// </summary>
    public static IServiceCollection AddRedisDslFlowStore(
        this IServiceCollection services,
        string? prefix = null)
    {
        EnsureRedisConnectionRegistered(services);
        prefix ??= RedisKeyPrefixes.DslFlow;
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
        string? prefix = null)
    {
        EnsureRedisConnectionRegistered(services);
        prefix ??= RedisKeyPrefixes.Flow;
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

    /// <summary>
    /// Add Redis event store for event sourcing.
    /// </summary>
    public static IServiceCollection AddRedisEventStore(
        this IServiceCollection services,
        string? prefix = null,
        IEventTypeRegistry? registry = null)
    {
        EnsureRedisConnectionRegistered(services);
        prefix ??= RedisKeyPrefixes.Events;
        services.TryAddSingleton<IEventStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            var logger = sp.GetRequiredService<ILogger<RedisEventStore>>();
            return new RedisEventStore(redis, serializer, provider, logger, registry, prefix);
        });

        return services;
    }

    /// <summary>
    /// Add Redis dead letter queue.
    /// </summary>
    public static IServiceCollection AddRedisDeadLetterQueue(
        this IServiceCollection services,
        Action<RedisDeadLetterQueueOptions>? configure = null)
    {
        if (configure != null)
            services.Configure(configure);

        services.TryAddSingleton<IDeadLetterQueue, RedisDeadLetterQueue>();

        return services;
    }

    /// <summary>
    /// Add Redis projection checkpoint store.
    /// </summary>
    public static IServiceCollection AddRedisProjectionCheckpointStore(
        this IServiceCollection services,
        string? prefix = null)
    {
        EnsureRedisConnectionRegistered(services);
        prefix ??= RedisKeyPrefixes.ProjectionCheckpoint;
        services.TryAddSingleton<IProjectionCheckpointStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new RedisProjectionCheckpointStore(redis, provider, prefix);
        });

        return services;
    }

    /// <summary>
    /// Add Redis subscription store.
    /// </summary>
    public static IServiceCollection AddRedisSubscriptionStore(
        this IServiceCollection services,
        string? prefix = null)
    {
        EnsureRedisConnectionRegistered(services);
        prefix ??= RedisKeyPrefixes.Subscription;
        services.TryAddSingleton<ISubscriptionStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new RedisSubscriptionStore(redis, provider, prefix);
        });

        return services;
    }

    /// <summary>
    /// Add Redis enhanced snapshot store.
    /// </summary>
    public static IServiceCollection AddRedisEnhancedSnapshotStore(
        this IServiceCollection services,
        string? prefix = null)
    {
        EnsureRedisConnectionRegistered(services);
        prefix ??= RedisKeyPrefixes.SnapshotEnhanced;
        services.TryAddSingleton<IEnhancedSnapshotStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new RedisEnhancedSnapshotStore(redis, serializer, provider, prefix);
        });

        return services;
    }
}
