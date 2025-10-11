using Catga.Distributed;
using Catga.Distributed.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Distributed.Redis.DependencyInjection;

/// <summary>
/// Redis 分布式集群服务扩展
/// </summary>
public static class RedisClusterServiceCollectionExtensions
{
    /// <summary>
    /// 添加基于 Redis 的分布式集群（无锁）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="redisConnectionString">Redis 连接字符串</param>
    /// <param name="nodeId">当前节点 ID</param>
    /// <param name="endpoint">当前节点终结点</param>
    /// <param name="keyPrefix">Key 前缀（默认：catga:nodes:）</param>
    /// <param name="routingStrategy">路由策略（默认：ConsistentHash）</param>
    /// <param name="useSortedSet">是否使用 Sorted Set（默认：true，推荐持久化）</param>
    /// <param name="useStreams">是否使用 Redis Streams 传输（默认：true，推荐）</param>
    public static IServiceCollection AddRedisCluster(
        this IServiceCollection services,
        string redisConnectionString,
        string nodeId,
        string endpoint,
        string keyPrefix = "catga:nodes:",
        RoutingStrategyType routingStrategy = RoutingStrategyType.ConsistentHash,
        bool useSortedSet = true,
        bool useStreams = true)
    {
        // 注册 Redis 连接
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            return ConnectionMultiplexer.Connect(redisConnectionString);
        });

        // 注册节点信息
        services.AddSingleton(new NodeInfo
        {
            NodeId = nodeId,
            Endpoint = endpoint,
            LastSeen = DateTime.UtcNow,
            Load = 0
        });

        // 注册节点发现
        if (useSortedSet)
        {
            // 使用 Sorted Set（推荐）- 持久化、自动过期、原生 TTL
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var connection = sp.GetRequiredService<IConnectionMultiplexer>();
                var logger = sp.GetRequiredService<ILogger<RedisSortedSetNodeDiscovery>>();
                return new RedisSortedSetNodeDiscovery(
                    connection, 
                    logger,
                    sortedSetKey: keyPrefix,
                    nodeTtl: TimeSpan.FromMinutes(5));
            });
        }
        else
        {
            // 使用 Pub/Sub（轻量级）- 内存存储、无持久化
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var connection = sp.GetRequiredService<IConnectionMultiplexer>();
                var logger = sp.GetRequiredService<ILogger<RedisNodeDiscovery>>();
                return new RedisNodeDiscovery(connection, logger, keyPrefix);
            });
        }

        // 注册路由策略
        services.AddSingleton<IRoutingStrategy>(sp =>
        {
            var currentNode = sp.GetRequiredService<NodeInfo>();
            return CreateRoutingStrategy(routingStrategy, currentNode.NodeId);
        });

        // 注册分布式 Mediator（无锁）
        services.AddSingleton<IDistributedMediator, DistributedMediator>();

        // 注册心跳后台服务（无锁）
        services.AddHostedService<HeartbeatBackgroundService>();

        // 如果启用 Streams，注册传输
        if (useStreams)
        {
            services.AddSingleton<RedisStreamTransport>(sp =>
            {
                var connection = sp.GetRequiredService<IConnectionMultiplexer>();
                var logger = sp.GetRequiredService<ILogger<RedisStreamTransport>>();
                return new RedisStreamTransport(
                    connection,
                    logger,
                    streamKey: "catga:messages",
                    consumerGroup: $"group-{nodeId}",
                    consumerId: nodeId);
            });
        }

        return services;
    }

    private static IRoutingStrategy CreateRoutingStrategy(RoutingStrategyType type, string currentNodeId)
    {
        return type switch
        {
            RoutingStrategyType.RoundRobin => new RoundRobinRoutingStrategy(),
            RoutingStrategyType.ConsistentHash => new ConsistentHashRoutingStrategy(),
            RoutingStrategyType.LoadBased => new LoadBasedRoutingStrategy(),
            RoutingStrategyType.Random => new RandomRoutingStrategy(),
            RoutingStrategyType.LocalFirst => new LocalFirstRoutingStrategy(currentNodeId),
            _ => new ConsistentHashRoutingStrategy()
        };
    }
}

