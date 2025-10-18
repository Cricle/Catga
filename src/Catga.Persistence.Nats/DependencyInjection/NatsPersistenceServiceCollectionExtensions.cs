using Catga.EventSourcing;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence;
using Catga.Persistence.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.JetStream;

namespace Catga;

/// <summary>
/// DI extensions for NATS Persistence
/// </summary>
public static class NatsPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// 注册 NATS KV Event Store (使用 JetStream KV)
    /// </summary>
    public static IServiceCollection AddNatsKVEventStore(
        this IServiceCollection services,
        string? bucketName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IEventStore>(sp =>
        {
            var jetStream = sp.GetRequiredService<INatsJSContext>();
            return new NatsKVEventStore(jetStream, bucketName);
        });

        return services;
    }

    /// <summary>
    /// 注册 NATS KV Outbox Store (使用 JetStream KV)
    /// </summary>
    public static IServiceCollection AddNatsKVOutbox(
        this IServiceCollection services,
        string? bucketName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IOutboxStore>(sp =>
        {
            var jetStream = sp.GetRequiredService<INatsJSContext>();
            return new NatsKVOutboxStore(jetStream, bucketName);
        });

        return services;
    }

    /// <summary>
    /// 注册 NATS KV Inbox Store (使用 JetStream KV)
    /// </summary>
    public static IServiceCollection AddNatsKVInbox(
        this IServiceCollection services,
        string? bucketName = null,
        TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IInboxStore>(sp =>
        {
            var jetStream = sp.GetRequiredService<INatsJSContext>();
            return new NatsKVInboxStore(jetStream, bucketName, ttl);
        });

        return services;
    }

    /// <summary>
    /// 注册所有 NATS Persistence 组件 (EventStore + Outbox + Inbox)
    /// </summary>
    public static IServiceCollection AddNatsPersistence(
        this IServiceCollection services,
        Action<NatsPersistenceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new NatsPersistenceOptions();
        configure?.Invoke(options);

        services.AddNatsKVEventStore(options.EventStoreBucket);
        services.AddNatsKVOutbox(options.OutboxBucket);
        services.AddNatsKVInbox(options.InboxBucket, options.InboxTTL);

        return services;
    }
}

/// <summary>
/// Configuration options for NATS Persistence
/// </summary>
public sealed class NatsPersistenceOptions
{
    public string? EventStoreBucket { get; set; }
    public string? OutboxBucket { get; set; }
    public string? InboxBucket { get; set; }
    public TimeSpan? InboxTTL { get; set; }
}

