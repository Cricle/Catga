using Catga.Distributed.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Distributed.Redis.DependencyInjection;

/// <summary>Redis cluster service extensions</summary>
public static class RedisClusterServiceCollectionExtensions
{
    public static IServiceCollection AddRedisCluster(this IServiceCollection services, string redisConnectionString, string nodeId, string endpoint, string keyPrefix = "catga:nodes:", RoutingStrategyType routingStrategy = RoutingStrategyType.ConsistentHash, bool useSortedSet = true, bool useStreams = true)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddSingleton(new NodeInfo { NodeId = nodeId, Endpoint = endpoint, LastSeen = DateTime.UtcNow, Load = 0 });

        if (useSortedSet)
        {
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var connection = sp.GetRequiredService<IConnectionMultiplexer>();
                var logger = sp.GetRequiredService<ILogger<RedisSortedSetNodeDiscovery>>();
                return new RedisSortedSetNodeDiscovery(connection, logger, sortedSetKey: keyPrefix, nodeTtl: TimeSpan.FromMinutes(5));
            });
        }
        else
        {
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var connection = sp.GetRequiredService<IConnectionMultiplexer>();
                var logger = sp.GetRequiredService<ILogger<RedisNodeDiscovery>>();
                return new RedisNodeDiscovery(connection, logger, keyPrefix);
            });
        }

        services.AddSingleton<IRoutingStrategy>(sp =>
        {
            var currentNode = sp.GetRequiredService<NodeInfo>();
            return CreateRoutingStrategy(routingStrategy, currentNode.NodeId);
        });

        services.AddSingleton<IDistributedMediator, DistributedMediator>();
        services.AddHostedService<HeartbeatBackgroundService>();

        if (useStreams)
        {
            services.AddSingleton<RedisStreamTransport>(sp =>
            {
                var connection = sp.GetRequiredService<IConnectionMultiplexer>();
                var logger = sp.GetRequiredService<ILogger<RedisStreamTransport>>();
                var options = new RedisStreamOptions
                {
                    StreamKey = "catga:messages",
                    ConsumerGroup = $"group-{nodeId}",
                    ConsumerId = nodeId
                };
                return new RedisStreamTransport(connection, logger, options);
            });
        }

        return services;
    }

    private static IRoutingStrategy CreateRoutingStrategy(RoutingStrategyType type, string currentNodeId) => type switch
    {
        RoutingStrategyType.RoundRobin => new RoundRobinRoutingStrategy(),
        RoutingStrategyType.ConsistentHash => new ConsistentHashRoutingStrategy(),
        RoutingStrategyType.LoadBased => new LoadBasedRoutingStrategy(),
        RoutingStrategyType.Random => new RandomRoutingStrategy(),
        RoutingStrategyType.LocalFirst => new LocalFirstRoutingStrategy(currentNodeId),
        _ => new ConsistentHashRoutingStrategy()
    };
}
