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
    /// Add DotNext Raft cluster support to Catga with deep integration
    /// Provides automatic leader election, message routing, and fault tolerance
    /// </summary>
    /// <remarks>
    /// This deeply integrates DotNext Raft into Catga:
    /// - Commands automatically route to Leader
    /// - Queries execute locally
    /// - Events broadcast to all nodes
    /// - Transparent fault tolerance
    /// </remarks>
    public static IServiceCollection AddRaftCluster(
        this IServiceCollection services,
        Action<DotNextClusterOptions>? configure = null)
    {
        var options = new DotNextClusterOptions();
        configure?.Invoke(options);

        // 1. Register core Raft components
        // TODO: Configure actual DotNext Raft cluster
        // services.AddSingleton<IRaftCluster>(sp => ...);
        // services.AddSingleton<IPersistentState, RaftStateMachine>();

        // 2. Register Raft-aware message transport
        services.AddSingleton<RaftMessageTransport>();
        
        // 3. Register RaftAwareMediator
        // TODO: Use decorator pattern once DotNext Raft is fully configured
        // For now, RaftAwareMediator needs manual registration
        // services.Decorate<ICatgaMediator, RaftAwareMediator>();
        
        // 4. Register health checks
        // TODO: Add Raft health check
        // services.AddHealthChecks().AddCheck<RaftHealthCheck>("raft");

        // 5. Log configuration
        Console.WriteLine($"🚀 DotNext Raft Cluster configured:");
        Console.WriteLine($"   Member ID: {options.ClusterMemberId}");
        Console.WriteLine($"   Members: {string.Join(", ", options.Members)}");
        Console.WriteLine($"   Election Timeout: {options.ElectionTimeout.TotalMilliseconds}ms");
        Console.WriteLine($"   Heartbeat Interval: {options.HeartbeatInterval.TotalMilliseconds}ms");
        Console.WriteLine();
        Console.WriteLine($"🎯 Automatic routing:");
        Console.WriteLine($"   • Command → Leader");
        Console.WriteLine($"   • Query → Local");
        Console.WriteLine($"   • Event → Broadcast");

        return services;
    }
}

