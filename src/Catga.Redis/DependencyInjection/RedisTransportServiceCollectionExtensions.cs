using Catga.Inbox;
using Catga.Outbox;
using Catga.Redis;
using Catga.Redis.Persistence;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// Redis 传输层服务注册扩展
/// </summary>
public static class RedisTransportServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Redis 消息传输
    /// </summary>
    public static IServiceCollection AddRedisTransport(
        this IServiceCollection services,
        Action<RedisTransportOptions>? configure = null)
    {
        var options = new RedisTransportOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IMessageTransport, RedisMessageTransport>();

        return services;
    }

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
    /// 添加完整的 Redis 支持（传输 + 持久化）
    /// </summary>
    public static IServiceCollection AddRedisFullStack(
        this IServiceCollection services,
        Action<RedisTransportOptions>? configureTransport = null,
        Action<RedisOutboxOptions>? configureOutbox = null,
        Action<RedisInboxOptions>? configureInbox = null)
    {
        services.AddRedisTransport(configureTransport);
        services.AddRedisOutboxPersistence(configureOutbox);
        services.AddRedisInboxPersistence(configureInbox);

        return services;
    }
}

