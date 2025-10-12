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

/// <summary>Catga service registration extensions (AOT compatible)</summary>
public static class CatgaServiceCollectionExtensions
{
    public static IServiceCollection AddCatga(this IServiceCollection services, Action<CatgaOptions>? configureOptions = null)
    {
        var options = new CatgaOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<ICatgaMediator, CatgaMediator>();
        services.TryAddSingleton<IIdempotencyStore>(new ShardedIdempotencyStore(options.IdempotencyShardCount, TimeSpan.FromHours(options.IdempotencyRetentionHours)));

        if (options.EnableDeadLetterQueue)
            services.TryAddSingleton<IDeadLetterQueue>(sp => new InMemoryDeadLetterQueue(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryDeadLetterQueue>>(), options.DeadLetterQueueMaxSize));

        if (options.EnableLogging) services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        if (options.EnableTracing) services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>));
        if (options.EnableIdempotency) services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
        if (options.EnableValidation) services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        if (options.EnableRetry) services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));

        return services;
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

        if (options.EnableLogging) services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        if (options.EnableTracing) services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>));
        if (options.EnableIdempotency) services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
        if (options.EnableValidation) services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        if (options.EnableRetry) services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));

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
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(OutboxBehavior<,>));

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
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(InboxBehavior<,>));

        return services;
    }
}

/// <summary>Outbox configuration options</summary>
public class OutboxOptions
{
    public bool EnablePublisher { get; set; } = true;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 100;
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>Inbox configuration options</summary>
public class InboxOptions
{
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}

