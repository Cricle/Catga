namespace Catga.Persistence.Redis;

/// <summary>
/// Redis Outbox options. Only Catga-specific settings, Redis connection via IConnectionMultiplexer.
/// </summary>
public record RedisOutboxOptions
{
    /// <summary>Key prefix for outbox messages.</summary>
    public string KeyPrefix { get; init; } = "catga:outbox:";

    /// <summary>Retention period for published messages.</summary>
    public TimeSpan PublishedRetention { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Polling interval for pending messages.</summary>
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Batch size for processing.</summary>
    public int BatchSize { get; init; } = 100;
}

