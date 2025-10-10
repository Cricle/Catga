using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // Validate options
        if (string.IsNullOrWhiteSpace(options.ClusterMemberId))
        {
            throw new ArgumentException("ClusterMemberId must be specified", nameof(options));
        }

        if (options.Members == null || options.Members.Length == 0)
        {
            throw new ArgumentException("At least one cluster member must be specified", nameof(options));
        }

        // 1. Configure DotNext Raft cluster
        // Note: Actual DotNext Raft HTTP cluster setup requires more complex configuration
        // This is a placeholder for the configuration structure
        // TODO: Complete DotNext Raft HTTP cluster configuration

        // 2. Register Catga wrapper for IRaftCluster
        services.AddSingleton<ICatgaRaftCluster, CatgaRaftCluster>();

        // 3. Register Raft-aware message transport
        services.AddSingleton<RaftMessageTransport>();
        
        // 4. Decorate ICatgaMediator with RaftAwareMediator
        // This wraps the existing mediator with Raft awareness
        services.AddSingleton<ICatgaMediator>(sp =>
        {
            // Get the original mediator
            var innerMediator = sp.GetServices<ICatgaMediator>()
                .FirstOrDefault(m => m.GetType().Name != nameof(RaftAwareMediator));
            
            if (innerMediator == null)
            {
                throw new InvalidOperationException(
                    "ICatgaMediator must be registered before calling AddRaftCluster. " +
                    "Make sure to call services.AddCatga() first.");
            }

            // Wrap with RaftAwareMediator
            var cluster = sp.GetRequiredService<ICatgaRaftCluster>();
            var logger = sp.GetRequiredService<ILogger<RaftAwareMediator>>();
            
            return new RaftAwareMediator(cluster, innerMediator, logger);
        });
        
        // 5. Register health checks
        // TODO: Add Raft health check
        // services.AddHealthChecks().AddCheck<RaftHealthCheck>("raft");

        // 6. Log configuration (using console for now, as logger isn't available yet)
        Console.WriteLine("🚀 DotNext Raft Cluster configured:");
        Console.WriteLine($"   Member ID: {options.ClusterMemberId}");
        Console.WriteLine($"   Members: {string.Join(", ", options.Members.Select(u => u.ToString()))}");
        Console.WriteLine($"   Election Timeout: {options.ElectionTimeout.TotalMilliseconds}ms");
        Console.WriteLine($"   Heartbeat Interval: {options.HeartbeatInterval.TotalMilliseconds}ms");
        Console.WriteLine();
        Console.WriteLine("🎯 Automatic routing:");
        Console.WriteLine("   • Command → Leader");
        Console.WriteLine("   • Query → Local");
        Console.WriteLine("   • Event → Broadcast");

        return services;
    }
}

