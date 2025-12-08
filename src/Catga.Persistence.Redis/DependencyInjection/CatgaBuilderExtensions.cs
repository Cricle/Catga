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
    /// </summary>
    public static CatgaServiceBuilder UseRedis(this CatgaServiceBuilder builder, string? connectionString = null)
    {
        if (!string.IsNullOrEmpty(connectionString))
        {
            builder.Services.AddRedisPersistence(connectionString);
        }
        else
        {
            builder.Services.AddRedisPersistence();
        }
        builder.Services.AddRedisEventStore();
        builder.Services.AddRedisSnapshotStore();
        builder.Services.AddRedisDeadLetterQueue();
        builder.Services.AddRedisDistributedLock();
        builder.Services.AddRedisRateLimiter();
        builder.Services.AddRedisMessageScheduler();
        builder.Services.AddRedisFlowStore();
        builder.Services.AddRedisDslFlowStore();
        return builder;
    }
}
