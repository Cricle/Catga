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
    /// 🚀 添加 Catga（流式配置 API）
    /// </summary>
    public static CatgaBuilder AddCatgaBuilder(
        this IServiceCollection services,
        Action<CatgaBuilder>? configure = null)
    {
        var options = new CatgaOptions();
        var builder = new CatgaBuilder(services, options);

        // 注册核心服务
        services.AddSingleton(options);
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();
        services.TryAddSingleton<IIdempotencyStore>(new ShardedIdempotencyStore(
            options.IdempotencyShardCount,
            TimeSpan.FromHours(options.IdempotencyRetentionHours)));

        configure?.Invoke(builder);

        // 应用配置后的选项
        if (options.EnableDeadLetterQueue)
            services.TryAddSingleton<IDeadLetterQueue>(sp =>
                new InMemoryDeadLetterQueue(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryDeadLetterQueue>>(),
                    options.DeadLetterQueueMaxSize));

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

        return builder;
    }

    /// <summary>
    /// 🎯 快速启动 - 开发模式（自动扫描 + 完整功能）
    /// ⚠️ 警告: 使用反射扫描，不完全兼容 NativeAOT
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("使用程序集扫描，不兼容 NativeAOT。生产环境请使用手动注册。")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("类型扫描可能需要动态代码生成")]
    public static IServiceCollection AddCatgaDevelopment(this IServiceCollection services)
    {
        return services.AddCatgaBuilder(builder => builder
            .ScanCurrentAssembly()
            .Configure(opt =>
            {
                opt.EnableLogging = true;
                opt.EnableTracing = true;
                opt.EnableValidation = true;
                opt.EnableIdempotency = true;
                opt.EnableRetry = true;
                opt.EnableDeadLetterQueue = true;
            })).ServiceCollection();
    }

    /// <summary>
    /// 🚀 快速启动 - 生产模式（性能优化 + 可靠性）
    /// ⚠️ 警告: 使用反射扫描，不完全兼容 NativeAOT
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("使用程序集扫描，不兼容 NativeAOT。生产环境请使用手动注册。")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("类型扫描可能需要动态代码生成")]
    public static IServiceCollection AddCatgaProduction(this IServiceCollection services)
    {
        return services.AddCatgaBuilder(builder => builder
            .ScanCurrentAssembly()
            .WithPerformanceOptimization()
            .WithReliability()
        ).ServiceCollection();
    }

    /// <summary>
    /// 获取 IServiceCollection（用于链式调用）
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("使用反射访问私有字段")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("可能需要动态代码生成")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "访问 CatgaBuilder 的已知私有字段")]
    private static IServiceCollection ServiceCollection(this CatgaBuilder builder)
    {
        return builder.GetType().GetField("_services",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(builder) as IServiceCollection ?? throw new InvalidOperationException();
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
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OutboxPublisher>>();

                return new OutboxPublisher(
                    store,
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
