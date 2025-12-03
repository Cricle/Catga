using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// Extension methods for distributed features registration.
/// </summary>
public static class DistributedExtensions
{
    /// <summary>Add distributed lock support.</summary>
    public static CatgaServiceBuilder UseDistributedLock<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TLock>(this CatgaServiceBuilder builder)
        where TLock : class, IDistributedLock
    {
        builder.Services.TryAddSingleton<IDistributedLock, TLock>();
        return builder;
    }

    /// <summary>Add distributed lock with options.</summary>
    public static CatgaServiceBuilder UseDistributedLock<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TLock>(
        this CatgaServiceBuilder builder,
        Action<DistributedLockOptions> configure)
        where TLock : class, IDistributedLock
    {
        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<IDistributedLock, TLock>();
        return builder;
    }

    /// <summary>Add message scheduler support.</summary>
    public static CatgaServiceBuilder UseMessageScheduler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TScheduler>(this CatgaServiceBuilder builder)
        where TScheduler : class, IMessageScheduler
    {
        builder.Services.TryAddSingleton<IMessageScheduler, TScheduler>();
        return builder;
    }

    /// <summary>Add message scheduler with options.</summary>
    public static CatgaServiceBuilder UseMessageScheduler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TScheduler>(
        this CatgaServiceBuilder builder,
        Action<MessageSchedulerOptions> configure)
        where TScheduler : class, IMessageScheduler
    {
        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<IMessageScheduler, TScheduler>();
        return builder;
    }

    /// <summary>Add snapshot store support.</summary>
    public static CatgaServiceBuilder UseSnapshotStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(this CatgaServiceBuilder builder)
        where TStore : class, ISnapshotStore
    {
        builder.Services.TryAddSingleton<ISnapshotStore, TStore>();
        return builder;
    }

    /// <summary>Add snapshot store with options.</summary>
    public static CatgaServiceBuilder UseSnapshotStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(
        this CatgaServiceBuilder builder,
        Action<SnapshotOptions> configure)
        where TStore : class, ISnapshotStore
    {
        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<ISnapshotStore, TStore>();
        return builder;
    }

    /// <summary>Add event version registry.</summary>
    public static CatgaServiceBuilder UseEventVersioning(this CatgaServiceBuilder builder)
    {
        builder.Services.TryAddSingleton<IEventVersionRegistry, EventVersionRegistry>();
        return builder;
    }

    /// <summary>Add event version registry with upgraders.</summary>
    public static CatgaServiceBuilder UseEventVersioning(
        this CatgaServiceBuilder builder,
        Action<IEventVersionRegistry> configure)
    {
        var registry = new EventVersionRegistry();
        configure(registry);
        builder.Services.TryAddSingleton<IEventVersionRegistry>(registry);
        return builder;
    }

    /// <summary>Add snapshot strategy.</summary>
    public static CatgaServiceBuilder UseSnapshotStrategy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy>(this CatgaServiceBuilder builder)
        where TStrategy : class, ISnapshotStrategy
    {
        builder.Services.TryAddSingleton<ISnapshotStrategy, TStrategy>();
        return builder;
    }

    /// <summary>Add event count snapshot strategy.</summary>
    public static CatgaServiceBuilder UseEventCountSnapshots(this CatgaServiceBuilder builder, int eventThreshold = 100)
    {
        builder.Services.TryAddSingleton<ISnapshotStrategy>(new EventCountSnapshotStrategy(eventThreshold));
        return builder;
    }

    /// <summary>Add aggregate repository.</summary>
    public static CatgaServiceBuilder AddAggregateRepository<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(this CatgaServiceBuilder builder)
        where TAggregate : class, IAggregateRoot, new()
    {
        builder.Services.TryAddScoped<IAggregateRepository<TAggregate>, AggregateRepository<TAggregate>>();
        return builder;
    }
}
