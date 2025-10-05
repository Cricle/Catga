using Catga;
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Redis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Redis Transit 依赖注入扩展
/// </summary>
public static class RedisCatgaServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Redis 持久化支持（包含 CatGa Store）
    /// </summary>
    public static IServiceCollection AddRedisCatga(
        this IServiceCollection services,
        Action<RedisCatgaOptions>? configureOptions = null)
    {
        var options = new RedisCatgaOptions();
        configureOptions?.Invoke(options);

        // 注册 Redis 连接
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            config.ConnectTimeout = options.ConnectTimeout;
            config.SyncTimeout = options.SyncTimeout;
            config.AllowAdmin = options.AllowAdmin;
            config.KeepAlive = options.KeepAlive;
            config.ConnectRetry = options.ConnectRetry;
            config.Ssl = options.UseSsl;

            if (!string.IsNullOrEmpty(options.SslHost))
            {
                config.SslHost = options.SslHost;
            }

            var logger = sp.GetRequiredService<ILogger<IConnectionMultiplexer>>();
            logger.LogInformation("Connecting to Redis at {ConnectionString}", options.ConnectionString);

            return ConnectionMultiplexer.Connect(config);
        });

        // 注册选项
        services.TryAddSingleton(options);

        // 注册 Redis CatGa Store
        services.TryAddSingleton<RedisCatGaStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisCatGaStore(redis, options);
        });

        // 注册 Redis 幂等性存储
        services.TryAddSingleton<IIdempotencyStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<RedisIdempotencyStore>>();
            return new RedisIdempotencyStore(redis, logger, options);
        });

        return services;
    }

    /// <summary>
    /// 添加 Redis CatGa Store（单独使用）
    /// </summary>
    public static IServiceCollection AddRedisCatGaStore(
        this IServiceCollection services,
        Action<RedisCatgaOptions>? configureOptions = null)
    {
        var options = new RedisCatgaOptions();
        configureOptions?.Invoke(options);

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            config.ConnectTimeout = options.ConnectTimeout;
            config.SyncTimeout = options.SyncTimeout;
            return ConnectionMultiplexer.Connect(config);
        });

        services.TryAddSingleton(options);

        services.TryAddSingleton<RedisCatGaStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisCatGaStore(redis, options);
        });

        return services;
    }

    /// <summary>
    /// 添加 Redis 幂等性存储（单独使用）
    /// </summary>
    public static IServiceCollection AddRedisIdempotencyStore(
        this IServiceCollection services,
        Action<RedisCatgaOptions>? configureOptions = null)
    {
        var options = new RedisCatgaOptions();
        configureOptions?.Invoke(options);

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            config.ConnectTimeout = options.ConnectTimeout;
            config.SyncTimeout = options.SyncTimeout;
            return ConnectionMultiplexer.Connect(config);
        });

        services.TryAddSingleton(options);

        services.TryAddSingleton<IIdempotencyStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<RedisIdempotencyStore>>();
            return new RedisIdempotencyStore(redis, logger, options);
        });

        return services;
    }

    /// <summary>
    /// 添加 Redis Outbox 存储（生产环境推荐）
    /// 确保消息发送的可靠性
    /// </summary>
    public static IServiceCollection AddRedisOutbox(
        this IServiceCollection services,
        Action<RedisCatgaOptions>? configureOptions = null)
    {
        var options = new RedisCatgaOptions();
        configureOptions?.Invoke(options);

        // 确保 Redis 连接已注册
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            config.ConnectTimeout = options.ConnectTimeout;
            config.SyncTimeout = options.SyncTimeout;
            return ConnectionMultiplexer.Connect(config);
        });

        services.TryAddSingleton(options);

        // 注册 Redis Outbox Store
        services.TryAddSingleton<IOutboxStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<RedisOutboxStore>>();
            return new RedisOutboxStore(redis, logger, options);
        });

        // 添加 Outbox Behavior
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(OutboxBehavior<,>));

        // 添加 Outbox Publisher 后台服务
        services.AddHostedService(sp =>
        {
            var store = sp.GetRequiredService<IOutboxStore>();
            var mediator = sp.GetRequiredService<ICatgaMediator>();
            var logger = sp.GetRequiredService<ILogger<OutboxPublisher>>();

            return new OutboxPublisher(
                store,
                mediator,
                logger,
                options.OutboxPollingInterval,
                options.OutboxBatchSize);
        });

        return services;
    }

    /// <summary>
    /// 添加 Redis Inbox 存储（生产环境推荐）
    /// 确保消息处理的幂等性
    /// </summary>
    public static IServiceCollection AddRedisInbox(
        this IServiceCollection services,
        Action<RedisCatgaOptions>? configureOptions = null)
    {
        var options = new RedisCatgaOptions();
        configureOptions?.Invoke(options);

        // 确保 Redis 连接已注册
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            config.ConnectTimeout = options.ConnectTimeout;
            config.SyncTimeout = options.SyncTimeout;
            return ConnectionMultiplexer.Connect(config);
        });

        services.TryAddSingleton(options);

        // 注册 Redis Inbox Store
        services.TryAddSingleton<IInboxStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<RedisInboxStore>>();
            return new RedisInboxStore(redis, logger, options);
        });

        // 添加 Inbox Behavior
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(InboxBehavior<,>));

        return services;
    }
}

