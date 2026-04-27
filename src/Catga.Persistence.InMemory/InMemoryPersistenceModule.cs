using Catga.Abstractions;
using Catga.DeadLetter;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Flow.Persistence;
using Catga.Idempotency;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence;
using Catga.Persistence.InMemory.Flow;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Catga.Persistence.InMemory;

/// <summary>
/// InMemory persistence module - registers all InMemory store implementations.
/// Implements IFlowPersistenceProvider to support Flow DSL stores.
/// </summary>
public sealed class InMemoryPersistenceModule : IPersistenceModule, IFlowPersistenceProvider
{
    public string Name => "InMemory";

    /// <summary>Options for InMemory persistence.</summary>
    public InMemoryPersistenceOptions Options { get; } = new();

    /// <summary>Maximum dead letter queue size.</summary>
    public int DeadLetterMaxSize { get; set; } = 1000;

    /// <summary>Directory for file-based distributed locks.</summary>
    public string? LockDirectory { get; set; }

    public void RegisterServices(IServiceCollection services)
    {
        services.TryAddSingleton<IEventStore>(sp => new InMemoryEventStore(sp.GetRequiredService<IResiliencePipelineProvider>()));
        services.TryAddSingleton<IOutboxStore>(sp => new MemoryOutboxStore(sp.GetRequiredService<IResiliencePipelineProvider>()));
        services.TryAddSingleton<IInboxStore>(sp => new MemoryInboxStore(sp.GetRequiredService<IResiliencePipelineProvider>()));
        services.TryAddSingleton<IDeadLetterQueue>(sp => new InMemoryDeadLetterQueue(
            sp.GetRequiredService<ILogger<InMemoryDeadLetterQueue>>(),
            sp.GetRequiredService<IMessageSerializer>(),
            DeadLetterMaxSize));
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.TryAddSingleton<ISnapshotStore, InMemorySnapshotStore>();

        var lockDir = new DirectoryInfo(LockDirectory ?? Path.Combine(Path.GetTempPath(), "catga-locks"));
        if (!lockDir.Exists) lockDir.Create();
        services.TryAddSingleton<IDistributedLockProvider>(new FileDistributedSynchronizationProvider(lockDir));

        services.TryAddSingleton<IFlowStore, InMemoryFlowStore>();
        services.TryAddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
    }

    // IFlowPersistenceProvider
    public IDslFlowStore? CreateDslFlowStore() => new InMemoryDslFlowStore();
    public IFlowStore? CreateFlowStore() => new InMemoryFlowStore();

    // IPersistenceProvider
    public IOutboxStore? CreateOutboxStore() => null; // registered via DI
    public IInboxStore? CreateInboxStore() => null;
    public IEventStore? CreateEventStore() => null;
    public IIdempotencyStore? CreateIdempotencyStore() => null;
    public IDeadLetterQueue? CreateDeadLetterQueue() => null;
    public ISnapshotStore? CreateSnapshotStore() => null;
    public IDistributedLockProvider? CreateDistributedLockProvider() => null;
    public IProjectionCheckpointStore? CreateProjectionCheckpointStore() => null;
}

public static class InMemoryPersistenceModuleExtensions
{
    public static IServiceCollection AddInMemoryPersistenceModule(
        this IServiceCollection services,
        Action<InMemoryPersistenceModule>? configure = null)
        => services.AddPersistenceModule(configure);
}
