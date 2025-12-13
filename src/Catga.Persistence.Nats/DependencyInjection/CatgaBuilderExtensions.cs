using Catga.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// CatgaServiceBuilder extensions for NATS persistence.
/// </summary>
public static class NatsCatgaBuilderExtensions
{
    /// <summary>
    /// Use NATS JetStream persistence for all stores (production).
    /// Requires INatsConnection to be registered.
    /// Registers all stores for feature parity with InMemory and Redis providers.
    /// Also registers default resilience pipeline if not already registered.
    /// </summary>
    public static CatgaServiceBuilder UseNats(this CatgaServiceBuilder builder, Action<NatsPersistenceOptions>? configure = null)
    {
        // Ensure core services are registered
        builder.UseResilience();
        builder.UseEventSourcing();

        // Core persistence (EventStore, Inbox, Outbox, Idempotency, Snapshot, DLQ)
        builder.Services.AddNatsPersistence(configure);

        // Enhanced stores
        builder.Services.AddNatsEnhancedSnapshotStore();

        // Flow stores
        builder.Services.AddNatsFlowStore();
        builder.Services.AddNatsDslFlowStore();

        // Event sourcing advanced
        builder.Services.AddNatsProjectionCheckpointStore();
        builder.Services.AddNatsSubscriptionStore();
        builder.Services.AddNatsAuditLogStore();

        // Distributed features
        builder.Services.AddNatsDistributedLock();
        builder.Services.AddNatsRateLimiter();
        builder.Services.AddNatsMessageScheduler();

        // Compliance
        builder.Services.AddNatsGdprStore();

        return builder;
    }
}
