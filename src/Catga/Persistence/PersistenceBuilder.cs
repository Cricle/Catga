using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Locking;
using Catga.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.Persistence;

/// <summary>
/// Fluent builder for registering persistence stores from a provider.
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

    /// <summary>Add DSL flow store.</summary>
    public PersistenceBuilder AddDslFlowStore()
    {
        var store = _provider.CreateDslFlowStore();
        if (store != null) _services.TryAddSingleton(store);
        return this;
    }

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

    /// <summary>Add flow store (saga).</summary>
    public PersistenceBuilder AddFlowStore()
    {
        var store = _provider.CreateFlowStore();
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

    /// <summary>Add all available stores from the provider.</summary>
    public PersistenceBuilder AddAll()
    {
        return AddDslFlowStore()
            .AddOutbox()
            .AddInbox()
            .AddEventStore()
            .AddIdempotency()
            .AddDeadLetterQueue()
            .AddSnapshotStore()
            .AddDistributedLock()
            .AddFlowStore()
            .AddProjectionCheckpoint();
    }
}

/// <summary>
/// Extension methods for persistence builder.
/// </summary>
public static class PersistenceBuilderExtensions
{
    /// <summary>
    /// Add persistence from a provider with fluent configuration.
    /// </summary>
    public static PersistenceBuilder AddPersistence(
        this IServiceCollection services,
        IPersistenceProvider provider)
    {
        return new PersistenceBuilder(services, provider);
    }
}
