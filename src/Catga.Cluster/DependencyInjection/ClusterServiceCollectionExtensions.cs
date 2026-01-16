using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Pipeline;
using DotNext.Net.Cluster.Consensus.Raft;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catga.Cluster.DependencyInjection;

/// <summary>
/// Extension methods for registering Catga cluster services.
/// </summary>
public static class ClusterServiceCollectionExtensions
{
    /// <summary>
    /// Adds Catga cluster coordinator using DotNext Raft.
    /// Requires IRaftCluster to be registered (e.g., via DotNext.AspNetCore.Cluster).
    /// </summary>
    public static IServiceCollection AddCatgaCluster(this IServiceCollection services, Action<ClusterOptions>? configure = null)
    {
        var options = new ClusterOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IClusterCoordinator>(sp =>
        {
            var cluster = sp.GetRequiredService<IRaftCluster>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<ClusterCoordinator>>();
            return new ClusterCoordinator(cluster, logger);
        });

        // Register HTTP forwarder if enabled
        if (options.EnableHttpForwarder)
        {
            services.AddHttpClient<IClusterForwarder, HttpClusterForwarder>(client =>
            {
                client.Timeout = options.ForwardTimeout;
            });
        }

        return services;
    }

    /// <summary>
    /// Adds leader-only behavior to the pipeline.
    /// Commands will fail if not executed on leader node.
    /// </summary>
    public static IServiceCollection AddLeaderOnlyBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(this IServiceCollection services)
        where TRequest : IRequest<TResponse>
    {
        services.AddScoped<IPipelineBehavior<TRequest, TResponse>, LeaderOnlyBehavior<TRequest, TResponse>>();
        return services;
    }

    /// <summary>
    /// Adds forward-to-leader behavior to the pipeline.
    /// Commands will be forwarded to leader if not on leader node.
    /// </summary>
    [RequiresUnreferencedCode("JSON serialization and deserialization might require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization and deserialization might require types that cannot be statically analyzed.")]
    public static IServiceCollection AddForwardToLeaderBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(this IServiceCollection services)
        where TRequest : IRequest<TResponse>
    {
        services.AddScoped<IPipelineBehavior<TRequest, TResponse>, ForwardToLeaderBehavior<TRequest, TResponse>>();
        return services;
    }

    /// <summary>
    /// Registers a singleton task that only runs on the leader node.
    /// </summary>
    public static IServiceCollection AddSingletonTask<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTask>(this IServiceCollection services)
        where TTask : SingletonTaskRunner
    {
        services.AddHostedService<TTask>();
        return services;
    }

    /// <summary>
    /// Adds cluster health check to the health check builder.
    /// </summary>
    public static IHealthChecksBuilder AddClusterHealthCheck(this IHealthChecksBuilder builder)
    {
        builder.AddCheck<ClusterHealthCheck>("cluster", tags: new[] { "cluster", "ready" });
        return builder;
    }
}

/// <summary>
/// Configuration options for Catga cluster.
/// </summary>
public sealed class ClusterOptions
{
    /// <summary>
    /// Enable HTTP-based request forwarding to leader.
    /// </summary>
    public bool EnableHttpForwarder { get; set; } = true;

    /// <summary>
    /// Timeout for forwarding requests to leader.
    /// </summary>
    public TimeSpan ForwardTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
