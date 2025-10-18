namespace Catga.Transport;

/// <summary>
/// Configuration options for Redis transport
/// </summary>
public sealed class RedisTransportOptions
{
    /// <summary>
    /// Redis connection string
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Consumer group name for Redis Streams (QoS 1)
    /// </summary>
    public string? ConsumerGroup { get; set; }

    /// <summary>
    /// Consumer name for Redis Streams (QoS 1)
    /// </summary>
    public string? ConsumerName { get; set; }

    /// <summary>
    /// Default QoS level
    /// </summary>
    public QualityOfService DefaultQoS { get; set; } = QualityOfService.AtMostOnce;

    /// <summary>
    /// Maximum retry attempts for Stream operations
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Stream read batch size
    /// </summary>
    public int StreamBatchSize { get; set; } = 10;
}

