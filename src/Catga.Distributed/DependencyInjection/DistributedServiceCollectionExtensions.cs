using Catga.Distributed.Nats;
using Catga.Distributed.Redis;
using Catga.Distributed.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using StackExchange.Redis;

namespace Catga.Distributed.DependencyInjection;

/// <summary>
/// 分布式服务扩展（完全无锁设计）
/// </summary>
public static class DistributedServiceCollectionExtensions
{
    /// <summary>
    /// 添加基于 NATS 的分布式集群（无锁）
    /// </summary>
    /// <param name="routingStrategy">路由策略（默认 Round-Robin）</param>
    /// <param name="useJetStream">是否使用 JetStream KV Store（默认 true，推荐）</param>
    public static IServiceCollection AddNatsCluster(
        this IServiceCollection services,
        string natsUrl,
        string nodeId,
        string endpoint,
        string subjectPrefix = "catga.nodes",
        RoutingStrategyType routingStrategy = RoutingStrategyType.RoundRobin,
        bool useJetStream = true)
    {
        // 注册 NATS 连接
        services.AddSingleton<INatsConnection>(sp =>
        {
            var opts = NatsOpts.Default with { Url = natsUrl };
            return new NatsConnection(opts);
        });

        // 注册节点信息
        services.AddSingleton(new NodeInfo
        {
            NodeId = nodeId,
            Endpoint = endpoint,
            LastSeen = DateTime.UtcNow,
            Load = 0
        });

        // 注册节点发现（支持 JetStream KV Store 或 Pub/Sub）
        if (useJetStream)
        {
            // 使用 JetStream KV Store（原生持久化，推荐）
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var connection = sp.GetRequiredService<INatsConnection>();
                var logger = sp.GetRequiredService<ILogger<NatsJetStreamNodeDiscovery>>();
                return new NatsJetStreamNodeDiscovery(connection, logger);
            });
        }
        else
        {
            // 使用 Pub/Sub（内存，不推荐）
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var connection = sp.GetRequiredService<INatsConnection>();
                var logger = sp.GetRequiredService<ILogger<NatsNodeDiscovery>>();
                return new NatsNodeDiscovery(connection, logger, subjectPrefix);
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

        return services;
    }

    /// <summary>
    /// 添加基于 Redis 的分布式集群（无锁）
    /// </summary>
    /// <param name="routingStrategy">路由策略（默认 Consistent Hash）</param>
    /// <param name="useSortedSet">是否使用 Sorted Set（默认 true，推荐）</param>
    /// <param name="useStreams">是否使用 Redis Streams（默认 true，推荐）</param>
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

        // 注册节点发现（支持 Sorted Set 或传统模式）
        if (useSortedSet)
        {
            // 使用 Sorted Set（原生持久化，推荐）
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                var logger = sp.GetRequiredService<ILogger<RedisSortedSetNodeDiscovery>>();
                return new RedisSortedSetNodeDiscovery(redis, logger, keyPrefix.TrimEnd(':'));
            });
        }
        else
        {
            // 使用传统模式（不推荐）
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var redis = sp.GetRequiredService<IConnectionMultiplexer>();
                var logger = sp.GetRequiredService<ILogger<RedisNodeDiscovery>>();
                return new RedisNodeDiscovery(redis, logger, keyPrefix);
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

        return services;
    }

    /// <summary>
    /// 创建路由策略实例
    /// </summary>
    private static IRoutingStrategy CreateRoutingStrategy(RoutingStrategyType type, string currentNodeId)
    {
        return type switch
        {
            RoutingStrategyType.RoundRobin => new RoundRobinRoutingStrategy(),
            RoutingStrategyType.ConsistentHash => new ConsistentHashRoutingStrategy(),
            RoutingStrategyType.LoadBased => new LoadBasedRoutingStrategy(),
            RoutingStrategyType.Random => new RandomRoutingStrategy(),
            RoutingStrategyType.LocalFirst => new LocalFirstRoutingStrategy(currentNodeId),
            _ => new RoundRobinRoutingStrategy()
        };
    }
}

