using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Outbox;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Persistence;

/// <summary>
/// Fluent builder for registering persistence stores from a provider.
/// For Flow DSL stores, use PersistenceBuilderFlowExtensions from Catga.Flow.
/// </summary>
public sealed class PersistenceBuilder
{
    private readonly IServiceCollection _services;
    private readonly IPersistenceProvider _provider;

    public PersistenceBuilder(IServiceCollection services, IPersistenceProvider provider)
    {
        _services = services;
        _provider = provider;
    }

    public IServiceCollection Services => _services;
    public IPersistenceProvider Provider => _provider;

    /// <summary>Add outbox store.</summary>
    public PersistenceBuilder AddOutbox()
    {
        var store = _provider.CreateOutboxStore();
        if (store != null) _services.TryAddSingleton(store);
        return this;
    }

    /// <summary>Add inbox store.</summary>
    public PersistenceBuilder AddInbox()
    {
        var store = _provider.CreateInboxStore();
        if (store != null) _services.TryAddSingleton(store);
        return this;
    }

    /// <summary>Add event store.</summary>
    public PersistenceBuilder AddEventStore()
    {
        var store = _provider.CreateEventStore();
        if (store != null) _services.TryAddSingleton(store);
        return this;
    }

    /// <summary>Add idempotency store.</summary>
    public PersistenceBuilder AddIdempotency()
    {
        var store = _provider.CreateIdempotencyStore();
        if (store != null) _services.TryAddSingleton(store);
        return this;
    }

    /// <summary>Add dead letter queue.</summary>
    public PersistenceBuilder AddDeadLetterQueue()
    {
        var store = _provider.CreateDeadLetterQueue();
        if (store != null) _services.TryAddSingleton(store);
        return this;
    }

    /// <summary>Add snapshot store.</summary>
    public PersistenceBuilder AddSnapshotStore()
    {
        var store = _provider.CreateSnapshotStore();
        if (store != null) _services.TryAddSingleton(store);
        return this;
    }

    /// <summary>Add distributed lock provider.</summary>
    public PersistenceBuilder AddDistributedLock()
    {
        var store = _provider.CreateDistributedLockProvider();
        if (store != null) _services.TryAddSingleton(store);
        return this;
    }

    /// <summary>Add projection checkpoint store.</summary>
    public PersistenceBuilder AddProjectionCheckpoint()
    {
        var store = _provider.CreateProjectionCheckpointStore();
        if (store != null) _services.TryAddSingleton(store);
        return this;
    }

    /// <summary>Add all available stores from the provider (excluding Flow stores).</summary>
    public PersistenceBuilder AddAll()
    {
        return AddOutbox()
            .AddInbox()
            .AddEventStore()
            .AddIdempotency()
            .AddDeadLetterQueue()
            .AddSnapshotStore()
            .AddDistributedLock()
            .AddProjectionCheckpoint();
    }
}

/// <summary>
/// Extension methods for persistence builder.
/// </summary>
public static class PersistenceBuilderExtensions
{
    public static PersistenceBuilder AddPersistence(
        this IServiceCollection services,
        IPersistenceProvider provider)
        => new(services, provider);
}
