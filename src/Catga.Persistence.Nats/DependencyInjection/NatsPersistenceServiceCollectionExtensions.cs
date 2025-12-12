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
using Catga.Scheduling;
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
        Action<NatsJSIdempotencyStoreOptions>? configureIdempotency = null,
        Action<NatsJSStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureIdempotency != null)
            services.Configure(configureIdempotency);

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
    /// Adds NATS JetStream-based audit log store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsAuditLogStore(
        this IServiceCollection services,
        Action<NatsJSStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAuditLogStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var options = Options.Create(new NatsJSStoreOptions());
            configure?.Invoke(options.Value);
            return new NatsJSAuditLogStore(connection, serializer, options);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS KV-based GDPR store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsGdprStore(
        this IServiceCollection services,
        Action<NatsJSStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IGdprStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var options = Options.Create(new NatsJSStoreOptions());
            configure?.Invoke(options.Value);
            return new NatsJSGdprStore(connection, serializer, options);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS KV-based encryption key store to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsEncryptionKeyStore(
        this IServiceCollection services,
        Action<NatsJSStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IEncryptionKeyStore>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var options = Options.Create(new NatsJSStoreOptions());
            configure?.Invoke(options.Value);
            return new NatsJSEncryptionKeyStore(connection, options);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS KV-based distributed lock to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsDistributedLock(
        this IServiceCollection services,
        Action<DistributedLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure != null)
            services.Configure(configure);
        else
            services.TryAddSingleton(Options.Create(new DistributedLockOptions()));

        services.TryAddSingleton<IDistributedLock>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var options = sp.GetRequiredService<IOptions<DistributedLockOptions>>();
            var logger = sp.GetRequiredService<ILogger<Catga.Persistence.Nats.Locking.NatsDistributedLock>>();
            return new Catga.Persistence.Nats.Locking.NatsDistributedLock(connection, options, logger);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS KV-based rate limiter to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsRateLimiter(
        this IServiceCollection services,
        Action<DistributedRateLimiterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure != null)
            services.Configure(configure);
        else
            services.TryAddSingleton(Options.Create(new DistributedRateLimiterOptions()));

        services.TryAddSingleton<IDistributedRateLimiter>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var options = sp.GetRequiredService<IOptions<DistributedRateLimiterOptions>>();
            return new Catga.Persistence.Nats.RateLimiting.NatsRateLimiter(connection, options);
        });

        return services;
    }

    /// <summary>
    /// Adds NATS KV-based message scheduler to the service collection.
    /// </summary>
    public static IServiceCollection AddNatsMessageScheduler(
        this IServiceCollection services,
        Action<MessageSchedulerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure != null)
            services.Configure(configure);
        else
            services.TryAddSingleton(Options.Create(new MessageSchedulerOptions()));

        services.TryAddSingleton<IMessageScheduler>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var mediator = sp.GetRequiredService<ICatgaMediator>();
            var options = sp.GetRequiredService<IOptions<MessageSchedulerOptions>>();
            return new Catga.Persistence.Nats.Scheduling.NatsMessageScheduler(connection, serializer, mediator, options);
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
