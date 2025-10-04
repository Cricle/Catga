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
/// Simplified Transit service registration (100% AOT-compatible)
/// </summary>
public static class TransitServiceCollectionExtensions
{
    /// <summary>
    /// Add Transit with sensible defaults
    /// </summary>
    public static IServiceCollection AddTransit(
        this IServiceCollection services,
        Action<CatgaOptions>? configureOptions = null)
    {
        var options = new CatgaOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();

        // High-performance sharded idempotency store
        services.TryAddSingleton<IIdempotencyStore>(new ShardedIdempotencyStore(
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

        // Register pipeline behaviors (order matters!)
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
    /// Register request handler (explicit, AOT-friendly)
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
    /// Register request handler without response
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
    /// Register event handler
    /// </summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(
        this IServiceCollection services)
        where TEvent : IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.AddTransient<IEventHandler<TEvent>, THandler>();
        return services;
    }

    /// <summary>
    /// Register validator
    /// </summary>
    public static IServiceCollection AddValidator<TRequest, TValidator>(
        this IServiceCollection services)
        where TValidator : class, IValidator<TRequest>
    {
        services.AddTransient<IValidator<TRequest>, TValidator>();
        return services;
    }
}
