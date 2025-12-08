namespace Catga.Persistence.Redis;

/// <summary>
/// Redis Inbox options. Only Catga-specific settings, Redis connection via IConnectionMultiplexer.
/// </summary>
public record RedisInboxOptions
{
    /// <summary>Key prefix for inbox messages.</summary>
    public string KeyPrefix { get; init; } = "catga:inbox:";

    /// <summary>Retention period for processed messages.</summary>
    public TimeSpan ProcessedRetention { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Default lock duration.</summary>
    public TimeSpan DefaultLockDuration { get; init; } = TimeSpan.FromMinutes(5);
}

