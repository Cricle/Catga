using Catga.Configuration;
using Catga.DeadLetter;
using Catga.Handlers;
using Catga.Idempotency;
using Catga.Messages;
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
    public static IServiceCollection AddRequestHandler<TRequest, TResponse, THandler>(
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
    public static IServiceCollection AddRequestHandler<TRequest, THandler>(
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
    public static IServiceCollection AddEventHandler<TEvent, THandler>(
        this IServiceCollection services)
        where TEvent : IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.AddTransient<IEventHandler<TEvent>, THandler>();
        return services;
    }
}
