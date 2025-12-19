using Catga.DependencyInjection;
using Catga.Persistence.InMemory.Flow;

namespace Catga.DependencyInjection;

/// <summary>
/// CatgaServiceBuilder extensions for InMemory persistence.
/// </summary>
public static class InMemoryCatgaBuilderExtensions
{
    /// <summary>
    /// Use InMemory persistence for all stores (development/testing).
    /// Registers all stores for feature parity with Redis and NATS providers.
    /// Also registers default resilience pipeline if not already registered.
    /// </summary>
    public static CatgaServiceBuilder UseInMemory(this CatgaServiceBuilder builder)
    {
        // Ensure core services are registered
        builder.UseResilience();
        builder.UseEventSourcing();

        // Core persistence (EventStore, SnapshotStore, Inbox, Outbox, DLQ, Idempotency, Lock, Flow)
        builder.Services.AddInMemoryPersistence();

        // Enhanced stores
        builder.Services.AddInMemoryEnhancedSnapshotStore();
        builder.Services.AddInMemoryDslFlowStore();

        // Event sourcing advanced
        builder.Services.AddInMemoryProjectionCheckpointStore();
        builder.Services.AddInMemorySubscriptionStore();

        return builder;
    }
}
