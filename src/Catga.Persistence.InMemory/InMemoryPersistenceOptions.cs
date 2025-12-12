namespace Catga.Persistence.InMemory;

/// <summary>
/// Unified options for all InMemory persistence stores.
/// </summary>
public class InMemoryPersistenceOptions
{
    /// <summary>Retention period for idempotency records. Default: 24 hours.</summary>
    public TimeSpan IdempotencyRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Default permit limit per rate limit window. Default: 100.</summary>
    public int RateLimitDefaultLimit { get; set; } = 100;

    /// <summary>Rate limit sliding window duration. Default: 1 minute.</summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);
}
