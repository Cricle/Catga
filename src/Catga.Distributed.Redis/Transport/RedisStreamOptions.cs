namespace Catga.Distributed.Redis;

/// <summary>
/// Redis Stream transport options with QoS support
/// </summary>
public record RedisStreamOptions
{
    /// <summary>Redis Stream key</summary>
    public string StreamKey { get; init; } = "catga:messages";

    /// <summary>Consumer group name</summary>
    public string ConsumerGroup { get; init; } = "catga-group";

    /// <summary>Consumer ID (null for auto-generated)</summary>
    public string? ConsumerId { get; init; }

    /// <summary>Maximum retry attempts for failed messages (QoS 1/2)</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Minimum idle time (ms) before retrying a pending message</summary>
    public long MinIdleTimeMs { get; init; } = 30000; // 30 seconds

    /// <summary>Interval for checking pending messages</summary>
    public TimeSpan PendingCheckInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Enable Dead Letter Queue for failed messages</summary>
    public bool EnableDLQ { get; init; } = true;

    /// <summary>Maximum messages to read per batch</summary>
    public int BatchSize { get; init; } = 10;
}

