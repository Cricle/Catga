using Catga.Abstractions;
using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.InMemory;
using Catga.Persistence.InMemory.Flow;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Medallion.Threading.WaitHandles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// Extension methods for setting up InMemory persistence services in an <see cref="IServiceCollection" />.
/// InMemory implementations are ideal for development, testing, and single-node scenarios.
/// </summary>
public static class InMemoryPersistenceServiceCollectionExtensions
{
    /// <summary>Adds InMemory event store to the service collection.</summary>
    public static IServiceCollection AddInMemoryEventStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IEventStore>(sp => new InMemoryEventStore(sp.GetRequiredService<IResiliencePipelineProvider>()));
        return services;
    }

    /// <summary>Adds InMemory outbox store to the service collection.</summary>
    public static IServiceCollection AddInMemoryOutboxStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IOutboxStore>(sp => new MemoryOutboxStore(sp.GetRequiredService<IResiliencePipelineProvider>()));
        return services;
    }

    /// <summary>Adds InMemory inbox store to the service collection.</summary>
    public static IServiceCollection AddInMemoryInboxStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IInboxStore>(sp => new MemoryInboxStore(sp.GetRequiredService<IResiliencePipelineProvider>()));
        return services;
    }

    /// <summary>Adds InMemory dead letter queue to the service collection.</summary>
    public static IServiceCollection AddInMemoryDeadLetterQueue(this IServiceCollection services, int maxSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDeadLetterQueue>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryDeadLetterQueue>>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            return new InMemoryDeadLetterQueue(logger, serializer, maxSize);
        });
        return services;
    }

    /// <summary>Adds InMemory idempotency store to the service collection.</summary>
    public static IServiceCollection AddInMemoryIdempotencyStore(this IServiceCollection services, Action<InMemoryPersistenceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure != null) services.Configure(configure);
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        return services;
    }

    /// <summary>Adds InMemory snapshot store to the service collection.</summary>
    public static IServiceCollection AddInMemorySnapshotStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISnapshotStore, InMemorySnapshotStore>();
        return services;
    }

    /// <summary>Adds InMemory enhanced snapshot store with version history support.</summary>
    public static IServiceCollection AddInMemoryEnhancedSnapshotStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IEnhancedSnapshotStore>(sp => new InMemoryEnhancedSnapshotStore(sp.GetRequiredService<IMessageSerializer>()));
        services.TryAddSingleton<ISnapshotStore>(sp => sp.GetRequiredService<IEnhancedSnapshotStore>());
        return services;
    }

    /// <summary>Adds file-based distributed lock to the service collection using DistributedLock.FileSystem.</summary>
    public static IServiceCollection AddInMemoryDistributedLock(this IServiceCollection services, string? lockDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var dir = new DirectoryInfo(lockDirectory ?? Path.Combine(Path.GetTempPath(), "catga-locks"));
        if (!dir.Exists) dir.Create();
        services.TryAddSingleton<IDistributedLockProvider>(new FileDistributedSynchronizationProvider(dir));
        return services;
    }

    /// <summary>Adds WaitHandle-based distributed lock to the service collection (in-process only, for testing).</summary>
    public static IServiceCollection AddWaitHandleDistributedLock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDistributedLockProvider>(new WaitHandleDistributedSynchronizationProvider());
        return services;
    }

    /// <summary>Adds InMemory flow store to the service collection.</summary>
    public static IServiceCollection AddInMemoryFlowStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFlowStore, InMemoryFlowStore>();
        return services;
    }

    /// <summary>Adds InMemory DSL flow store to the service collection.</summary>
    public static IServiceCollection AddInMemoryDslFlowStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<Catga.Flow.Dsl.IDslFlowStore, InMemoryDslFlowStore>();
        return services;
    }

    /// <summary>Adds InMemory projection checkpoint store to the service collection.</summary>
    public static IServiceCollection AddInMemoryProjectionCheckpointStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IProjectionCheckpointStore, InMemoryProjectionCheckpointStore>();
        return services;
    }

    /// <summary>Adds InMemory subscription store to the service collection.</summary>
    public static IServiceCollection AddInMemorySubscriptionStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISubscriptionStore, InMemorySubscriptionStore>();
        return services;
    }

    /// <summary>Adds complete InMemory persistence (all stores) to the service collection.</summary>
    public static IServiceCollection AddInMemoryPersistence(this IServiceCollection services, int deadLetterMaxSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddInMemoryEventStore();
        services.AddInMemoryOutboxStore();
        services.AddInMemoryInboxStore();
        services.AddInMemoryDeadLetterQueue(deadLetterMaxSize);
        services.AddInMemoryIdempotencyStore();
        services.AddInMemorySnapshotStore();
        services.AddInMemoryDistributedLock();
        services.AddInMemoryFlowStore();
        return services;
    }
}
