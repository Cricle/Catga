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
    /// </summary>
    public static CatgaServiceBuilder UseNats(this CatgaServiceBuilder builder, Action<NatsPersistenceOptions>? configure = null)
    {
        builder.Services.AddNatsPersistence(configure);
        builder.Services.AddNatsProjectionCheckpointStore();
        builder.Services.AddNatsSubscriptionStore();
        builder.Services.AddNatsAuditLogStore();
        builder.Services.AddNatsDistributedLock();
        builder.Services.AddNatsRateLimiter();
        builder.Services.AddNatsMessageScheduler();
        return builder;
    }
}
