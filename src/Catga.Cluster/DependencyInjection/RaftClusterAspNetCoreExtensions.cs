using DotNext.Net.Cluster.Consensus.Raft;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Cluster.DependencyInjection;

/// <summary>
/// Extensions for setting up Catga Raft cluster from configuration.
/// For ASP.NET Core HTTP transport, also call app.UseConsensusProtocolHandler()
/// from DotNext.AspNetCore.Cluster package.
/// </summary>
public static class RaftClusterAspNetCoreExtensions
{
    /// <summary>
    /// Add Catga cluster coordinator from IConfiguration.
    /// Requires IRaftCluster to be registered (via DotNext.AspNetCore.Cluster).
    /// </summary>
    /// <example>
    /// // In Program.cs:
    /// builder.Services.AddCatgaRaftCluster(builder.Configuration);
    /// // Then in app setup:
    /// app.UseConsensusProtocolHandler(); // from DotNext.AspNetCore.Cluster
    /// </example>
    public static IServiceCollection AddCatgaRaftCluster(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ClusterOptions>? configure = null)
    {
        var config = RaftClusterConfiguration.FromConfiguration(configuration);
        services.AddSingleton(config);
        services.AddCatgaCluster(configure);
        return services;
    }

    /// <summary>
    /// Add Catga cluster coordinator with explicit configuration.
    /// </summary>
    public static IServiceCollection AddCatgaRaftCluster(
        this IServiceCollection services,
        RaftClusterConfiguration config,
        Action<ClusterOptions>? configure = null)
    {
        services.AddSingleton(config);
        services.AddCatgaCluster(configure);
        return services;
    }
}
