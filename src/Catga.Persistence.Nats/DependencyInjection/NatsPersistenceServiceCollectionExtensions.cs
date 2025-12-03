using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence;
using Catga.Persistence.Stores;
using Catga.DeadLetter;
using Catga.Idempotency;
using Catga.Persistence.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.Core;
using Catga.Resilience;

namespace Catga;

/// <summary>
/// Extension methods for setting up NATS JetStream persistence services in an <see cref="IServiceCollection" />.
/// </summary>
public static class NatsPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Adds NATS JetStream-based event store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsEventStore(
        this IServiceCollection services,
        string? streamName = null,
        Action<NatsJSStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IEventStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();

            var options = new NatsJSStoreOptions { StreamName = streamName ?? "CATGA_EVENTS" };
            configure?.Invoke(options);

            return new Catga.Persistence.NatsJSEventStore(connection, serializer, streamName, options, provider);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS JetStream-based dead letter queue to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsDeadLetterQueue(
        this IServiceCollection services,
        string? streamName = null,
        Action<NatsJSStoreOptions>? configure = null,
        IResiliencePipelineProvider? resiliencePipelineProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IDeadLetterQueue>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = resiliencePipelineProvider ?? sp.GetRequiredService<IResiliencePipelineProvider>();

            var options = new NatsJSStoreOptions { StreamName = streamName ?? "CATGA_DLQ" };
            configure?.Invoke(options);

            return new NatsJSDeadLetterQueue(connection, serializer, provider, streamName ?? "CATGA_DLQ", options);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS JetStream-based idempotency store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsIdempotencyStore(
        this IServiceCollection services,
        string? streamName = null,
        Action<NatsJSStoreOptions>? configure = null,
        IResiliencePipelineProvider? resiliencePipelineProvider = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IIdempotencyStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = resiliencePipelineProvider ?? sp.GetRequiredService<IResiliencePipelineProvider>();

            var options = new NatsJSStoreOptions { StreamName = streamName ?? "CATGA_IDEMPOTENCY" };
            configure?.Invoke(options);

            return new NatsJSIdempotencyStore(connection, serializer, provider, streamName ?? "CATGA_IDEMPOTENCY", null, options);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS JetStream-based outbox store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsOutboxStore(
        this IServiceCollection services,
        string? streamName = null,
        Action<NatsJSStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IOutboxStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();

            var options = new NatsJSStoreOptions { StreamName = streamName ?? "CATGA_OUTBOX" };
            configure?.Invoke(options);

            return new NatsJSOutboxStore(connection, serializer, provider, streamName, options);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS JetStream-based inbox store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsInboxStore(
        this IServiceCollection services,
        string? streamName = null,
        Action<NatsJSStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IInboxStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();

            var options = new NatsJSStoreOptions { StreamName = streamName ?? "CATGA_INBOX" };
            configure?.Invoke(options);

            return new NatsJSInboxStore(connection, serializer, provider, streamName, options);
        });

        return services;
    }

    /// <summary>
    /// Adds complete NATS JetStream persistence (EventStore + Outbox + Inbox) to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsPersistence(
        this IServiceCollection services,
        Action<NatsPersistenceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new NatsPersistenceOptions();
        configure?.Invoke(options);

        services.AddNatsEventStore(options.EventStreamName);
        services.AddNatsOutboxStore(options.OutboxStreamName);
        services.AddNatsInboxStore(options.InboxStreamName);
        services.AddNatsDeadLetterQueue();
        services.AddNatsIdempotencyStore();

        return services;
    }
}

/// <summary>
/// Configuration options for NATS JetStream Persistence
/// </summary>
public sealed class NatsPersistenceOptions
{
    /// <summary>
    /// JetStream stream name for Event Store (default: "CATGA_EVENTS")
    /// </summary>
    public string? EventStreamName { get; set; }

    /// <summary>
    /// JetStream stream name for Outbox Store (default: "CATGA_OUTBOX")
    /// </summary>
    public string? OutboxStreamName { get; set; }

    /// <summary>
    /// JetStream stream name for Inbox Store (default: "CATGA_INBOX")
    /// </summary>
    public string? InboxStreamName { get; set; }

    /// <summary>
    /// Store options for Event Store
    /// </summary>
    public NatsJSStoreOptions? EventStoreOptions { get; set; }

    /// <summary>
    /// Store options for Outbox Store
    /// </summary>
    public NatsJSStoreOptions? OutboxStoreOptions { get; set; }

    /// <summary>
    /// Store options for Inbox Store
    /// </summary>
    public NatsJSStoreOptions? InboxStoreOptions { get; set; }
}
