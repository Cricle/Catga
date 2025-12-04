using Catga.Inbox;
using Catga.Locking;
using Catga.Outbox;
using Catga.Persistence.Redis.Persistence;
using Catga.Persistence.Redis;
using Catga.Persistence.Redis.Locking;
using Catga.Persistence.Redis.RateLimiting;
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
}
