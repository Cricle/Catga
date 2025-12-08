namespace Catga.Persistence.Redis;

/// <summary>
/// Redis idempotency options. Redis connection via IConnectionMultiplexer.
/// </summary>
public record RedisIdempotencyOptions
{
    /// <summary>Key prefix for idempotency keys.</summary>
    public string KeyPrefix { get; init; } = "catga:idempotency:";

    /// <summary>Expiry time for idempotency records.</summary>
    public TimeSpan Expiry { get; init; } = TimeSpan.FromHours(24);
}

