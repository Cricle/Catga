using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence;
using Catga.Persistence.Stores;
using Catga.Persistence.Nats.Flow;
using Catga.Persistence.Nats.Stores;
using Catga.DeadLetter;
using Catga.Idempotency;
using Catga.Persistence.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
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

            return new Catga.Persistence.NatsJSEventStore(connection, serializer, provider, null, streamName, options);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS JetStream-based dead letter queue to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsDeadLetterQueue(
        this IServiceCollection services,
        Action<NatsJSStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure != null)
            services.Configure(configure);
        services.TryAddSingleton<IDeadLetterQueue, NatsJSDeadLetterQueue>();
        return services;
    }

    /// <summary>
    /// Adds NATS JetStream-based idempotency store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsIdempotencyStore(
        this IServiceCollection services,
        Action<NatsJSStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure != null)
            services.Configure(configure);
        services.TryAddSingleton<IIdempotencyStore, NatsJSIdempotencyStore>();
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
    /// Adds NATS KV-based flow store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsFlowStore(
        this IServiceCollection services,
        string? bucketName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IFlowStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            return new NatsFlowStore(connection, serializer, bucketName ?? "flows");
        });

        return services;
    }

    /// <summary>
    /// Adds NATS KV-based DSL flow store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsDslFlowStore(
        this IServiceCollection services,
        string? bucketName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IDslFlowStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            return new NatsDslFlowStore(connection, serializer, bucketName ?? "dslflows");
        });

        return services;
    }

    /// <summary>
    /// Adds NATS KV-based snapshot store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsSnapshotStore(
        this IServiceCollection services,
        Action<SnapshotOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure != null)
            services.Configure(configure);
        else
            services.TryAddSingleton(Options.Create(new SnapshotOptions()));

        services.TryAddSingleton<ISnapshotStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var options = sp.GetRequiredService<IOptions<SnapshotOptions>>();
            var logger = sp.GetRequiredService<ILogger<NatsSnapshotStore>>();
            return new NatsSnapshotStore(connection, serializer, options, logger);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS KV-based enhanced snapshot store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsEnhancedSnapshotStore(
        this IServiceCollection services,
        string? bucketName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IEnhancedSnapshotStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new NatsEnhancedSnapshotStore(connection, serializer, provider, bucketName ?? "enhanced-snapshots");
        });

        return services;
    }

    /// <summary>
    /// Adds NATS KV-based projection checkpoint store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsProjectionCheckpointStore(
        this IServiceCollection services,
        string? bucketName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IProjectionCheckpointStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new NatsProjectionCheckpointStore(connection, serializer, provider, bucketName ?? "projection-checkpoints");
        });

        return services;
    }

    /// <summary>
    /// Adds NATS KV-based subscription store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsSubscriptionStore(
        this IServiceCollection services,
        string? bucketName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISubscriptionStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new NatsSubscriptionStore(connection, serializer, provider, bucketName ?? "subscriptions");
        });

        return services;
    }

    /// <summary>
    /// Adds NATS connection to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsConnection(
        this IServiceCollection services,
        string natsUrl = "nats://localhost:4222")
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<INatsConnection>(sp =>
        {
            var opts = NatsOpts.Default with { Url = natsUrl };
            return new NatsConnection(opts);
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
        services.AddNatsFlowStore(options.FlowBucketName);
        services.AddNatsDslFlowStore(options.DslFlowBucketName);
        services.AddNatsSnapshotStore();

        return services;
    }
}

/// <summary>
/// NATS persistence options. Only stream/bucket names - use NATS native config for advanced settings.
/// </summary>
public sealed class NatsPersistenceOptions
{
    /// <summary>Stream name prefix (default: "CATGA").</summary>
    public string StreamPrefix { get; set; } = "CATGA";

    /// <summary>Event stream name (default: "{StreamPrefix}_EVENTS").</summary>
    public string? EventStreamName { get; set; }

    /// <summary>Outbox stream name (default: "{StreamPrefix}_OUTBOX").</summary>
    public string? OutboxStreamName { get; set; }

    /// <summary>Inbox stream name (default: "{StreamPrefix}_INBOX").</summary>
    public string? InboxStreamName { get; set; }

    /// <summary>Flow KV bucket name (default: "flows").</summary>
    public string FlowBucketName { get; set; } = "flows";

    /// <summary>DSL Flow KV bucket name (default: "dslflows").</summary>
    public string DslFlowBucketName { get; set; } = "dslflows";
}
