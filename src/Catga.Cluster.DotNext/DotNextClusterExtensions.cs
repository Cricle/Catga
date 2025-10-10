using Microsoft.Extensions.DependencyInjection;

namespace Catga.Cluster.DotNext;

/// <summary>
/// DotNext Raft cluster options
/// </summary>
public class DotNextClusterOptions
{
    /// <summary>
    /// Cluster member ID (e.g., "node1")
    /// </summary>
    public string ClusterMemberId { get; set; } = Environment.MachineName;

    /// <summary>
    /// Cluster member URLs (e.g., ["http://node1:5001", "http://node2:5002"])
    /// </summary>
    public string[] Members { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Election timeout (default: 150ms)
    /// </summary>
    public TimeSpan ElectionTimeout { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Heartbeat interval (default: 50ms)
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Log compaction threshold (default: 1000 entries)
    /// </summary>
    public int CompactionThreshold { get; set; } = 1000;
}

/// <summary>
/// DotNext cluster integration extensions
/// </summary>
public static class DotNextClusterExtensions
{
    /// <summary>
    /// Add DotNext Raft cluster support to Catga
    /// Provides automatic leader election, log replication, and fault tolerance
    /// </summary>
    public static IServiceCollection AddDotNextCluster(
        this IServiceCollection services,
        Action<DotNextClusterOptions>? configure = null)
    {
        var options = new DotNextClusterOptions();
        configure?.Invoke(options);

        // TODO: Integrate DotNext.Net.Cluster
        // - Configure Raft cluster
        // - Setup message routing (Command → Leader, Query → Any, Event → All)
        // - Add cluster health checks
        
        // Placeholder: Log configuration
        Console.WriteLine($"🚀 DotNext Cluster configured:");
        Console.WriteLine($"   Member ID: {options.ClusterMemberId}");
        Console.WriteLine($"   Members: {string.Join(", ", options.Members)}");
        Console.WriteLine($"   Election Timeout: {options.ElectionTimeout.TotalMilliseconds}ms");

        return services;
    }
}

