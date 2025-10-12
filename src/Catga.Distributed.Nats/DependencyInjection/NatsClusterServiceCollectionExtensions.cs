using Catga.Distributed.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Distributed.Nats.DependencyInjection;

/// <summary>NATS cluster service extensions</summary>
public static class NatsClusterServiceCollectionExtensions
{
    public static IServiceCollection AddNatsCluster(this IServiceCollection services, string natsUrl, string nodeId, string endpoint, string subjectPrefix = "catga.nodes", RoutingStrategyType routingStrategy = RoutingStrategyType.RoundRobin, bool useJetStream = true)
    {
        services.AddSingleton<INatsConnection>(sp =>
        {
            var opts = NatsOpts.Default with { Url = natsUrl };
            return new NatsConnection(opts);
        });

        services.AddSingleton(new NodeInfo { NodeId = nodeId, Endpoint = endpoint, LastSeen = DateTime.UtcNow, Load = 0 });

        if (useJetStream)
        {
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var connection = sp.GetRequiredService<INatsConnection>();
                var logger = sp.GetRequiredService<ILogger<NatsJetStreamKVNodeDiscovery>>();
                return new NatsJetStreamKVNodeDiscovery(connection, logger, bucketName: $"{subjectPrefix}_kv", nodeTtl: TimeSpan.FromMinutes(5));
            });
        }
        else
        {
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var connection = sp.GetRequiredService<INatsConnection>();
                var logger = sp.GetRequiredService<ILogger<NatsNodeDiscovery>>();
                return new NatsNodeDiscovery(connection, logger, subjectPrefix);
            });
        }

        services.AddSingleton<IRoutingStrategy>(sp =>
        {
            var currentNode = sp.GetRequiredService<NodeInfo>();
            return CreateRoutingStrategy(routingStrategy, currentNode.NodeId);
        });

        services.AddSingleton<IDistributedMediator, DistributedMediator>();
        services.AddHostedService<HeartbeatBackgroundService>();

        return services;
    }

    private static IRoutingStrategy CreateRoutingStrategy(RoutingStrategyType type, string currentNodeId) => type switch
    {
        RoutingStrategyType.RoundRobin => new RoundRobinRoutingStrategy(),
        RoutingStrategyType.ConsistentHash => new ConsistentHashRoutingStrategy(),
        RoutingStrategyType.LoadBased => new LoadBasedRoutingStrategy(),
        RoutingStrategyType.Random => new RandomRoutingStrategy(),
        RoutingStrategyType.LocalFirst => new LocalFirstRoutingStrategy(currentNodeId),
        _ => new RoundRobinRoutingStrategy()
    };
}

