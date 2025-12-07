using System;
using Catga.Transport;
using StackExchange.Redis;

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
/// Connection selection strategy for the pool
/// </summary>
public enum ConnectionSelectionStrategy
{
    /// <summary>
    /// Round-robin selection (default)
    /// </summary>
    RoundRobin,

    /// <summary>
    /// Load-based selection (least busy connection)
    /// </summary>
    LoadBased
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
    /// Consumer group name for Redis Streams (QoS 1)
    /// </summary>
    public string? ConsumerGroup { get; set; }

    /// <summary>
    /// Consumer name for Redis Streams (QoS 1)
    /// </summary>
    public string? ConsumerName { get; set; }

    /// <summary>
    /// Optional auto-batching configuration. When null, auto-batching is disabled by default.
    /// </summary>
    public BatchTransportOptions? Batch { get; set; }

    /// <summary>
    /// Upper bound on pending queue length when auto-batching is enabled. Oldest items will be dropped when exceeded.
    /// </summary>
    public int MaxQueueLength { get; set; } = 10000;

    /// <summary>
    /// Whether to register the connection pool in DI
    /// </summary>
    public bool RegistConnection { get; set; } = true;

    /// <summary>
    /// Redis configuration options
    /// </summary>
    public ConfigurationOptions? ConfigurationOptions { get; set; }

    /// <summary>
    /// Connection pool size. Default is Environment.ProcessorCount.
    /// </summary>
    public int PoolSize { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Connection selection strategy for the pool
    /// </summary>
    public ConnectionSelectionStrategy SelectionStrategy { get; set; } = ConnectionSelectionStrategy.RoundRobin;
}

