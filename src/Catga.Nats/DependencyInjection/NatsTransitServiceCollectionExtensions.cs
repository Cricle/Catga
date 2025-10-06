using Catga.Configuration;
using Catga.DeadLetter;
using Catga.DependencyInjection;
using Catga.Handlers;
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Messages;
using Catga.Outbox;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Catga.Nats.DependencyInjection;

/// <summary>
/// NATS Transit service registration with full Pipeline Behaviors support (AOT-compatible)
/// </summary>
public static class NatsCatgaServiceCollectionExtensions
{
    /// <summary>
    /// Add NATS Catga with full features
    /// </summary>
    public static IServiceCollection AddNatsCatga(
        this IServiceCollection services,
        string natsUrl,
        Action<CatgaOptions>? configureOptions = null)
    {
        var options = new CatgaOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);

        // Register NATS connection
        services.AddSingleton<INatsConnection>(sp =>
        {
            var opts = NatsOpts.Default with { Url = natsUrl };
            return new NatsConnection(opts);
        });

        // Register NATS mediator
        services.TryAddSingleton<ICatgaMediator, NatsCatgaMediator>();

        // Idempotency store (shared for subscriber side)
        services.TryAddSingleton<IIdempotencyStore>(sp =>
            new ShardedIdempotencyStore(
                options.IdempotencyShardCount,
                TimeSpan.FromHours(options.IdempotencyRetentionHours)));

        // Dead letter queue
        if (options.EnableDeadLetterQueue)
        {
            services.TryAddSingleton<IDeadLetterQueue>(sp =>
                new InMemoryDeadLetterQueue(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryDeadLetterQueue>>(),
                    options.DeadLetterQueueMaxSize));
        }

        return services;
    }

    /// <summary>
    /// Subscribe to NATS requests with full Pipeline support
    /// </summary>
    public static IServiceCollection SubscribeToNatsRequest<TRequest, TResponse>(
        this IServiceCollection services)
        where TRequest : IRequest<TResponse>
    {
        services.AddSingleton<NatsRequestSubscriber<TRequest, TResponse>>();
        return services;
    }

    /// <summary>
    /// Subscribe to NATS events
    /// </summary>
    public static IServiceCollection SubscribeToNatsEvent<TEvent>(
        this IServiceCollection services)
        where TEvent : IEvent
    {
        services.AddSingleton<NatsEventSubscriber<TEvent>>();
        return services;
    }

    /// <summary>
    /// 🚀 添加 NATS JetStream 持久化存储（与 Redis 功能对等）
    /// </summary>
    public static IServiceCollection AddNatsJetStreamStores(
        this IServiceCollection services,
        string natsUrl,
        Action<NatsJetStreamOptions>? configure = null)
    {
        var options = new NatsJetStreamOptions();
        configure?.Invoke(options);

        // 注册 NATS JetStream
        services.AddSingleton<INatsJSContext>(sp =>
        {
            var connection = sp.GetService<INatsConnection>();
            if (connection == null)
            {
                var opts = NatsOpts.Default with { Url = natsUrl };
                connection = new NatsConnection(opts);
            }

            return new NatsJSContext(connection);
        });

        return services;
    }

    /// <summary>
    /// 🗄️ 添加 NATS Outbox 存储
    /// </summary>
    public static IServiceCollection AddNatsOutbox(
        this IServiceCollection services,
        Action<OutboxOptions>? configureOptions = null)
    {
        var options = new OutboxOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IOutboxStore>(sp =>
        {
            var serializer = sp.GetRequiredService<Catga.Serialization.IMessageSerializer>();
            return new NatsOutboxStore(serializer);
        });

        // 添加 Outbox Behavior
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(OutboxBehavior<,>));

        // 添加 Outbox Publisher 后台服务
        if (options.EnablePublisher)
        {
            services.AddHostedService(sp =>
            {
                var store = sp.GetRequiredService<IOutboxStore>();
                var mediator = sp.GetRequiredService<ICatgaMediator>();
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OutboxPublisher>>();

                return new OutboxPublisher(
                    store,
                    mediator,
                    logger,
                    options.PollingInterval,
                    options.BatchSize);
            });
        }

        return services;
    }

    /// <summary>
    /// 📥 添加 NATS Inbox 存储
    /// </summary>
    public static IServiceCollection AddNatsInbox(
        this IServiceCollection services,
        Action<InboxOptions>? configureOptions = null)
    {
        var options = new InboxOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IInboxStore>(sp =>
        {
            var serializer = sp.GetRequiredService<Catga.Serialization.IMessageSerializer>();
            return new NatsInboxStore(serializer);
        });

        // 添加 Inbox Behavior
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(InboxBehavior<,>));

        return services;
    }

    /// <summary>
    /// 🔑 添加 NATS 幂等性存储
    /// </summary>
    public static IServiceCollection AddNatsIdempotency(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IIdempotencyStore>(sp =>
        {
            var serializer = sp.GetRequiredService<Catga.Serialization.IMessageSerializer>();
            return new NatsIdempotencyStore(serializer);
        });

        // 添加 Idempotency Behavior
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));

        return services;
    }

    /// <summary>
    /// 🌐 添加完整的 NATS 分布式支持（与 Redis 完全对等）
    /// </summary>
    public static IServiceCollection AddNatsDistributed(
        this IServiceCollection services,
        string natsUrl,
        Action<NatsDistributedOptions>? configure = null)
    {
        var options = new NatsDistributedOptions();
        configure?.Invoke(options);

        // 注册 NATS JetStream
        services.AddNatsJetStreamStores(natsUrl);

        // 注册所有存储
        if (options.EnableOutbox)
            services.AddNatsOutbox(options.OutboxOptions);

        if (options.EnableInbox)
            services.AddNatsInbox(options.InboxOptions);

        if (options.EnableIdempotency)
            services.AddNatsIdempotency();

        // 注册 NATS Mediator
        services.AddNatsCatga(natsUrl, opt =>
        {
            opt.EnableLogging = options.EnableLogging;
            opt.EnableTracing = options.EnableTracing;
            opt.EnableRetry = options.EnableRetry;
            opt.EnableDeadLetterQueue = options.EnableDeadLetterQueue;
        });

        return services;
    }
}

/// <summary>
/// NATS JetStream 配置选项
/// </summary>
public class NatsJetStreamOptions
{
    public string StreamPrefix { get; set; } = "CATGA";
    public int MaxAge { get; set; } = 24; // 小时
}

/// <summary>
/// NATS 分布式配置选项（与 Redis 对等）
/// </summary>
public class NatsDistributedOptions
{
    public bool EnableOutbox { get; set; } = true;
    public bool EnableInbox { get; set; } = true;
    public bool EnableIdempotency { get; set; } = true;
    public bool EnableLogging { get; set; } = true;
    public bool EnableTracing { get; set; } = true;
    public bool EnableRetry { get; set; } = true;
    public bool EnableDeadLetterQueue { get; set; } = true;

    public Action<OutboxOptions>? OutboxOptions { get; set; }
    public Action<InboxOptions>? InboxOptions { get; set; }
}
