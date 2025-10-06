using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.Redis.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// Redis 持久化存储服务注册扩展
/// </summary>
public static class RedisPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Redis Outbox 持久化存储
    /// </summary>
    public static IServiceCollection AddRedisOutboxPersistence(
        this IServiceCollection services,
        Action<RedisOutboxOptions>? configure = null)
    {
        var options = new RedisOutboxOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IOutboxStore, RedisOutboxPersistence>();

        return services;
    }

    /// <summary>
    /// 添加 Redis Inbox 持久化存储
    /// </summary>
    public static IServiceCollection AddRedisInboxPersistence(
        this IServiceCollection services,
        Action<RedisInboxOptions>? configure = null)
    {
        var options = new RedisInboxOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IInboxStore, RedisInboxPersistence>();

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

        return services;
    }
}

