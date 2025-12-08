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
    /// Registers: EventStore, SnapshotStore, Inbox, Outbox, DLQ, Idempotency, Lock, Flow, RateLimiter, Scheduler.
    /// </summary>
    public static CatgaServiceBuilder UseInMemory(this CatgaServiceBuilder builder)
    {
        builder.Services.AddInMemoryPersistence();
        builder.Services.AddInMemoryRateLimiter();
        builder.Services.AddInMemoryMessageScheduler();
        builder.Services.AddInMemoryDslFlowStore();
        return builder;
    }
}
