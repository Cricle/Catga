namespace Catga.Persistence.Redis;

/// <summary>
/// Unified Redis persistence options for all Redis-based stores.
/// Redis connection via IConnectionMultiplexer.
/// </summary>
public record RedisPersistenceOptions
{
    /// <summary>Key prefix for idempotency keys.</summary>
    public string IdempotencyKeyPrefix { get; init; } = "catga:idempotency:";

    /// <summary>Expiry time for idempotency records.</summary>
    public TimeSpan IdempotencyExpiry { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Key prefix for inbox messages.</summary>
    public string InboxKeyPrefix { get; init; } = "catga:inbox:";

    /// <summary>Retention period for processed inbox messages.</summary>
    public TimeSpan InboxProcessedRetention { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Default lock duration for inbox.</summary>
    public TimeSpan InboxDefaultLockDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Key prefix for outbox messages.</summary>
    public string OutboxKeyPrefix { get; init; } = "catga:outbox:";

    /// <summary>Retention period for published outbox messages.</summary>
    public TimeSpan OutboxPublishedRetention { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Polling interval for pending outbox messages.</summary>
    public TimeSpan OutboxPollingInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Batch size for outbox processing.</summary>
    public int OutboxBatchSize { get; init; } = 100;
}
