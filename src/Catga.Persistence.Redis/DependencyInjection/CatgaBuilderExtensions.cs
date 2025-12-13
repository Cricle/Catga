using Catga.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// CatgaServiceBuilder extensions for Redis persistence.
/// </summary>
public static class RedisCatgaBuilderExtensions
{
    /// <summary>
    /// Use Redis persistence for all stores (production).
    /// Requires IConnectionMultiplexer to be registered or provide connectionString.
    /// Registers all stores for feature parity with InMemory and NATS providers.
    /// </summary>
    public static CatgaServiceBuilder UseRedis(this CatgaServiceBuilder builder, string? connectionString = null)
    {
        // Core persistence (Inbox, Outbox, Idempotency)
        if (!string.IsNullOrEmpty(connectionString))
        {
            builder.Services.AddRedisPersistence(connectionString);
        }
        else
        {
            builder.Services.AddRedisPersistence();
        }

        // Event sourcing
        builder.Services.AddRedisEventStore();
        builder.Services.AddRedisSnapshotStore();
        builder.Services.AddRedisEnhancedSnapshotStore();
        builder.Services.AddRedisDeadLetterQueue();

        // Flow stores
        builder.Services.AddRedisFlowStore();
        builder.Services.AddRedisDslFlowStore();

        // Event sourcing advanced
        builder.Services.AddRedisProjectionCheckpointStore();
        builder.Services.AddRedisSubscriptionStore();
        builder.Services.AddRedisAuditLogStore();

        // Distributed features
        builder.Services.AddRedisDistributedLock();
        builder.Services.AddRedisRateLimiter();
        builder.Services.AddRedisMessageScheduler();

        // Compliance
        builder.Services.AddRedisGdprStore();

        return builder;
    }
}
