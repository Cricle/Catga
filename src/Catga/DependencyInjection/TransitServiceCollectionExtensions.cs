using System.Diagnostics.CodeAnalysis;
using Catga.Configuration;
using Catga.DeadLetter;
using Catga.Handlers;
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Messages;
using Catga.Outbox;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// 精简的 Catga 服务注册扩展（100% AOT 兼容）
/// </summary>
public static class CatgaServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Catga 服务到 DI 容器
    /// </summary>
    public static IServiceCollection AddCatga(
        this IServiceCollection services,
        Action<CatgaOptions>? configureOptions = null)
    {
        var options = new CatgaOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();

        // 高性能分片幂等存储
        services.TryAddSingleton<IIdempotencyStore>(new ShardedIdempotencyStore(
            options.IdempotencyShardCount,
            TimeSpan.FromHours(options.IdempotencyRetentionHours)));

        // 死信队列
        if (options.EnableDeadLetterQueue)
            services.TryAddSingleton<IDeadLetterQueue>(sp =>
                new InMemoryDeadLetterQueue(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryDeadLetterQueue>>(),
                    options.DeadLetterQueueMaxSize));

        // 管道行为（顺序很重要！）
        if (options.EnableLogging)
            services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        if (options.EnableTracing)
            services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>));

        if (options.EnableIdempotency)
            services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));

        if (options.EnableValidation)
            services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        if (options.EnableRetry)
            services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));

        return services;
    }

    /// <summary>
    /// 注册请求处理器（显式，AOT 友好）
    /// </summary>
    public static IServiceCollection AddRequestHandler<TRequest, TResponse, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        services.AddTransient<IRequestHandler<TRequest, TResponse>, THandler>();
        return services;
    }

    /// <summary>
    /// 注册无响应请求处理器
    /// </summary>
    public static IServiceCollection AddRequestHandler<TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services)
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>
    {
        services.AddTransient<IRequestHandler<TRequest>, THandler>();
        return services;
    }

    /// <summary>
    /// 注册事件处理器
    /// </summary>
    public static IServiceCollection AddEventHandler<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services)
        where TEvent : IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.AddTransient<IEventHandler<TEvent>, THandler>();
        return services;
    }

    /// <summary>
    /// 添加 Outbox 模式支持（内存版本）
    /// 确保消息发送的可靠性
    /// </summary>
    public static IServiceCollection AddOutbox(
        this IServiceCollection services,
        Action<OutboxOptions>? configureOptions = null)
    {
        var options = new OutboxOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IOutboxStore, MemoryOutboxStore>();

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
    /// 添加 Inbox 模式支持（内存版本）
    /// 确保消息处理的幂等性
    /// </summary>
    public static IServiceCollection AddInbox(
        this IServiceCollection services,
        Action<InboxOptions>? configureOptions = null)
    {
        var options = new InboxOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IInboxStore, MemoryInboxStore>();

        // 添加 Inbox Behavior
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(InboxBehavior<,>));

        return services;
    }
}

/// <summary>
/// Outbox 配置选项
/// </summary>
public class OutboxOptions
{
    /// <summary>
    /// 是否启用 Outbox Publisher 后台服务
    /// </summary>
    public bool EnablePublisher { get; set; } = true;

    /// <summary>
    /// 轮询间隔（默认 5 秒）
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 每批次处理的消息数量（默认 100）
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// 消息保留时间（默认 24 小时）
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Inbox 配置选项
/// </summary>
public class InboxOptions
{
    /// <summary>
    /// 处理锁定时长（默认 5 分钟）
    /// </summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 消息保留时间（默认 24 小时）
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}
