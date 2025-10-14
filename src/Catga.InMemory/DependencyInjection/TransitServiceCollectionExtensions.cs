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

/// <summary>Catga service registration extensions (AOT compatible, serializer-agnostic)</summary>
public static class CatgaServiceCollectionExtensions
{
    /// <summary>
    /// Add Catga core services with fluent builder API
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Optional configuration action</param>
    /// <returns>CatgaServiceBuilder for fluent configuration</returns>
    /// <remarks>
    /// Note: Requires IMessageSerializer to be registered. Use one of:
    /// <code>
    /// services.AddCatga().UseMemoryPack();  // Recommended for AOT
    /// services.AddCatga().UseJson();         // Or JSON
    /// </code>
    /// 
    /// Complete example:
    /// <code>
    /// services.AddCatga()
    ///     .UseMemoryPack()
    ///     .ForProduction();
    /// </code>
    /// </remarks>
    public static CatgaServiceBuilder AddCatga(this IServiceCollection services, Action<CatgaOptions>? configureOptions = null)
    {
        var options = new CatgaOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();
        services.TryAddSingleton<IIdempotencyStore>(new ShardedIdempotencyStore(options.IdempotencyShardCount, TimeSpan.FromHours(options.IdempotencyRetentionHours)));

        if (options.EnableDeadLetterQueue)
            services.TryAddSingleton<IDeadLetterQueue>(sp => new InMemoryDeadLetterQueue(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryDeadLetterQueue>>(), options.DeadLetterQueueMaxSize));

        if (options.EnableLogging) services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        if (options.EnableTracing) services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>));
        if (options.EnableIdempotency) services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
        if (options.EnableValidation) services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        if (options.EnableRetry) services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));

        return new CatgaServiceBuilder(services, options);
    }

    public static CatgaBuilder AddCatgaBuilder(this IServiceCollection services, Action<CatgaBuilder>? configure = null)
    {
        var options = new CatgaOptions();
        var builder = new CatgaBuilder(services, options);

        services.AddSingleton(options);
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();
        services.TryAddSingleton<IIdempotencyStore>(new ShardedIdempotencyStore(options.IdempotencyShardCount, TimeSpan.FromHours(options.IdempotencyRetentionHours)));

        configure?.Invoke(builder);

        if (options.EnableDeadLetterQueue)
            services.TryAddSingleton<IDeadLetterQueue>(sp => new InMemoryDeadLetterQueue(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryDeadLetterQueue>>(), options.DeadLetterQueueMaxSize));

        if (options.EnableLogging) services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        if (options.EnableTracing) services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>));
        if (options.EnableIdempotency) services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
        if (options.EnableValidation) services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        if (options.EnableRetry) services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));

        return builder;
    }

    [RequiresUnreferencedCode("Uses assembly scanning, not compatible with NativeAOT")]
    [RequiresDynamicCode("Type scanning may require dynamic code generation")]
    public static IServiceCollection AddCatgaDevelopment(this IServiceCollection services)
        => services.AddCatgaBuilder(builder => builder
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

    [RequiresUnreferencedCode("Uses assembly scanning, not compatible with NativeAOT")]
    [RequiresDynamicCode("Type scanning may require dynamic code generation")]
    public static IServiceCollection AddCatgaProduction(this IServiceCollection services)
        => services.AddCatgaBuilder(builder => builder
            .ScanCurrentAssembly()
            .WithPerformanceOptimization()
            .WithReliability()).ServiceCollection();

    private static IServiceCollection ServiceCollection(this CatgaBuilder builder) => builder.Services;

    public static IServiceCollection AddRequestHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services) where TRequest : IRequest<TResponse> where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        services.AddTransient<IRequestHandler<TRequest, TResponse>, THandler>();
        return services;
    }

    public static IServiceCollection AddRequestHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services) where TRequest : IRequest where THandler : class, IRequestHandler<TRequest>
    {
        services.AddTransient<IRequestHandler<TRequest>, THandler>();
        return services;
    }

    public static IServiceCollection AddEventHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services) where TEvent : IEvent where THandler : class, IEventHandler<TEvent>
    {
        services.AddTransient<IEventHandler<TEvent>, THandler>();
        return services;
    }

    public static IServiceCollection AddOutbox(this IServiceCollection services, Action<OutboxOptions>? configureOptions = null)
    {
        var options = new OutboxOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IOutboxStore, MemoryOutboxStore>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OutboxBehavior<,>));

        if (options.EnablePublisher)
        {
            services.AddHostedService(sp =>
            {
                var store = sp.GetRequiredService<IOutboxStore>();
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OutboxPublisher>>();
                return new OutboxPublisher(store, logger, options.PollingInterval, options.BatchSize);
            });
        }

        return services;
    }

    public static IServiceCollection AddInbox(this IServiceCollection services, Action<InboxOptions>? configureOptions = null)
    {
        var options = new InboxOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IInboxStore, MemoryInboxStore>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InboxBehavior<,>));

        return services;
    }
}

/// <summary>Outbox configuration options (immutable record)</summary>
public record OutboxOptions
{
    public bool EnablePublisher { get; init; } = true;
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; init; } = 100;
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromHours(24);
}

/// <summary>Inbox configuration options (immutable record)</summary>
public record InboxOptions
{
    public TimeSpan LockDuration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromHours(24);
}

