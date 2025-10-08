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
/// Simplified Catga service registration extensions (100% AOT compatible)
/// </summary>
public static class CatgaServiceCollectionExtensions
{
    /// <summary>
    /// Add Catga services to DI container
    /// </summary>
    public static IServiceCollection AddCatga(
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
            services.TryAddSingleton<IDeadLetterQueue>(sp =>
                new InMemoryDeadLetterQueue(
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryDeadLetterQueue>>(),
                    options.DeadLetterQueueMaxSize));

        // Pipeline behaviors (order matters!)
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
    /// Add Catga with fluent configuration API
    /// </summary>
    public static CatgaBuilder AddCatgaBuilder(
        this IServiceCollection services,
        Action<CatgaBuilder>? configure = null)
    {
        var options = new CatgaOptions();
        var builder = new CatgaBuilder(services, options);

        // Register core services
        services.AddSingleton(options);
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();
        services.TryAddSingleton<IIdempotencyStore>(new ShardedIdempotencyStore(
            options.IdempotencyShardCount,
            TimeSpan.FromHours(options.IdempotencyRetentionHours)));

        configure?.Invoke(builder);

        // Apply configured options
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
    /// Quick start - Development mode (Auto-scan + Full features)
    /// WARNING: Uses reflection scanning, not fully compatible with NativeAOT
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses assembly scanning, not compatible with NativeAOT. Use manual registration in production.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Type scanning may require dynamic code generation")]
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
    /// Quick start - Production mode (Performance optimization + Reliability)
    /// WARNING: Uses reflection scanning, not fully compatible with NativeAOT
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses assembly scanning, not compatible with NativeAOT. Use manual registration in production.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Type scanning may require dynamic code generation")]
    public static IServiceCollection AddCatgaProduction(this IServiceCollection services)
    {
        return services.AddCatgaBuilder(builder => builder
            .ScanCurrentAssembly()
            .WithPerformanceOptimization()
            .WithReliability()
        ).ServiceCollection();
    }

    /// <summary>
    /// Get IServiceCollection (for chaining)
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses reflection to access private fields")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("May require dynamic code generation")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Accessing known private field of CatgaBuilder")]
    private static IServiceCollection ServiceCollection(this CatgaBuilder builder)
    {
        return builder.GetType().GetField("_services",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(builder) as IServiceCollection ?? throw new InvalidOperationException();
    }

    /// <summary>
    /// Register request handler (Explicit, AOT-friendly)
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
    /// Register request handler without response
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
    /// Register event handler
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
    /// Add Outbox pattern support (in-memory version)
    /// Ensures reliable message delivery
    /// </summary>
    [RequiresUnreferencedCode("Outbox behavior requires serialization support. Use AOT-friendly serializer like MemoryPack in production")]
    [RequiresDynamicCode("Outbox behavior requires serialization support. Use AOT-friendly serializer like MemoryPack in production")]
    public static IServiceCollection AddOutbox(
        this IServiceCollection services,
        Action<OutboxOptions>? configureOptions = null)
    {
        var options = new OutboxOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IOutboxStore, MemoryOutboxStore>();

        // Add Outbox Behavior
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(OutboxBehavior<,>));

        // Add Outbox Publisher background service
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
    /// Add Inbox pattern support (in-memory version)
    /// Ensures message processing idempotency
    /// </summary>
    [RequiresUnreferencedCode("Inbox behavior requires serialization support. Use AOT-friendly serializer like MemoryPack in production")]
    [RequiresDynamicCode("Inbox behavior requires serialization support. Use AOT-friendly serializer like MemoryPack in production")]
    public static IServiceCollection AddInbox(
        this IServiceCollection services,
        Action<InboxOptions>? configureOptions = null)
    {
        var options = new InboxOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IInboxStore, MemoryInboxStore>();

        // Add Inbox Behavior
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(InboxBehavior<,>));

        return services;
    }
}

/// <summary>
/// Outbox configuration options
/// </summary>
public class OutboxOptions
{
    /// <summary>
    /// Enable Outbox Publisher background service
    /// </summary>
    public bool EnablePublisher { get; set; } = true;

    /// <summary>
    /// Polling interval (default: 5 seconds)
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Batch size for message processing (default: 100)
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Message retention period (default: 24 hours)
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Inbox configuration options
/// </summary>
public class InboxOptions
{
    /// <summary>
    /// Processing lock duration (default: 5 minutes)
    /// </summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Message retention period (default: 24 hours)
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}
