using Catga.EventSourcing;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence;
using Catga.Persistence.Stores;
using Catga.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.Core;

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

            var options = new NatsJSStoreOptions { StreamName = streamName ?? "CATGA_EVENTS" };
            configure?.Invoke(options);

            return new NatsJSEventStore(connection, serializer, streamName, options);
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

            var options = new NatsJSStoreOptions { StreamName = streamName ?? "CATGA_OUTBOX" };
            configure?.Invoke(options);

            return new NatsJSOutboxStore(connection, serializer, streamName, options);
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

            var options = new NatsJSStoreOptions { StreamName = streamName ?? "CATGA_INBOX" };
            configure?.Invoke(options);

            return new NatsJSInboxStore(connection, serializer, streamName, options);
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
