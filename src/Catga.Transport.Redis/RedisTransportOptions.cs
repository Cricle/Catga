using System;
namespace Catga.Transport;

/// <summary>
/// Redis deployment mode
/// </summary>
public enum RedisMode
{
    /// <summary>
    /// Standalone Redis server
    /// </summary>
    Standalone,

    /// <summary>
    /// Redis Sentinel for high availability
    /// </summary>
    Sentinel,

    /// <summary>
    /// Redis Cluster for horizontal scaling
    /// </summary>
    Cluster
}

/// <summary>
/// Configuration options for Redis transport
/// </summary>
public sealed class RedisTransportOptions
{
    /// <summary>
    /// Logical channel prefix for Pub/Sub subject naming (default: "catga.")
    /// </summary>
    public string ChannelPrefix { get; set; } = "catga.";

    /// <summary>
    /// Optional channel naming convention: maps message type to channel suffix.
    /// Final channel will be ChannelPrefix + Naming(type).
    /// </summary>
    public Func<Type, string>? Naming { get; set; }

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

    // === Connection Settings ===

    /// <summary>
    /// Connection timeout in milliseconds (default: 5000ms)
    /// </summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>
    /// Synchronous operation timeout in milliseconds (default: 5000ms)
    /// </summary>
    public int SyncTimeout { get; set; } = 5000;

    /// <summary>
    /// Asynchronous operation timeout in milliseconds (default: 5000ms)
    /// </summary>
    public int AsyncTimeout { get; set; } = 5000;

    /// <summary>
    /// Whether to abort connection if initial connect fails (default: false)
    /// Set to true in production to fail-fast on configuration errors
    /// </summary>
    public bool AbortOnConnectFail { get; set; } = false;

    /// <summary>
    /// Client name for identification in Redis (default: "Catga")
    /// </summary>
    public string ClientName { get; set; } = "Catga";

    /// <summary>
    /// Whether to allow admin commands (default: false)
    /// Required for some management operations
    /// </summary>
    public bool AllowAdmin { get; set; } = false;

    // === High Availability & Clustering ===

    /// <summary>
    /// Redis deployment mode (default: Standalone)
    /// </summary>
    public RedisMode Mode { get; set; } = RedisMode.Standalone;

    /// <summary>
    /// Service name for Sentinel mode
    /// </summary>
    public string? SentinelServiceName { get; set; }

    /// <summary>
    /// Whether to use SSL/TLS for connection (default: false)
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// SSL/TLS host name (required if UseSsl is true)
    /// </summary>
    public string? SslHost { get; set; }

    // === Performance Settings ===

    /// <summary>
    /// Keep alive interval in seconds (default: 60s)
    /// -1 to disable keep-alive
    /// </summary>
    public int KeepAlive { get; set; } = 60;

    /// <summary>
    /// Retry count for connection establishment (default: 3)
    /// </summary>
    public int ConnectRetry { get; set; } = 3;

    /// <summary>
    /// Whether to respect asynchronous timeouts (default: true)
    /// </summary>
    public bool RespectAsyncTimeout { get; set; } = true;

    // === Connection Pool Settings ===

    /// <summary>
    /// Minimum thread pool size for asynchronous operations (default: 10)
    /// </summary>
    public int MinThreadPoolSize { get; set; } = 10;

    /// <summary>
    /// Default database index (default: 0)
    /// </summary>
    public int DefaultDatabase { get; set; } = 0;
}

