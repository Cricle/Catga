using Catga.Configuration;
using Catga.DeadLetter;
using Catga.Handlers;
using Catga.Idempotency;
using Catga.Messages;
using Catga.Pipeline.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.Core;

namespace Catga.Nats.DependencyInjection;

/// <summary>
/// NATS Transit service registration with full Pipeline Behaviors support (AOT-compatible)
/// </summary>
public static class NatsTransitServiceCollectionExtensions
{
    /// <summary>
    /// Add NATS Transit with full features
    /// </summary>
    public static IServiceCollection AddNatsTransit(
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
}
