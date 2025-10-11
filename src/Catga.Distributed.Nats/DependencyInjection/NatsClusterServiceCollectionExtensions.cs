using Catga.Distributed;
using Catga.Distributed.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Distributed.Nats.DependencyInjection;

/// <summary>
/// NATS 分布式集群服务扩展
/// </summary>
public static class NatsClusterServiceCollectionExtensions
{
    /// <summary>
    /// 添加基于 NATS 的分布式集群（无锁）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="natsUrl">NATS 服务器地址</param>
    /// <param name="nodeId">当前节点 ID</param>
    /// <param name="endpoint">当前节点终结点</param>
    /// <param name="subjectPrefix">Subject 前缀（默认：catga.nodes）</param>
    /// <param name="routingStrategy">路由策略（默认：RoundRobin）</param>
    /// <param name="useJetStream">是否使用 JetStream KV Store（默认：true，推荐持久化）</param>
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

        // 注册节点发现
        if (useJetStream)
        {
            // 使用 JetStream KV Store（推荐）- 持久化、历史记录、自动过期
            services.AddSingleton<INodeDiscovery>(sp =>
            {
                var connection = sp.GetRequiredService<INatsConnection>();
                var logger = sp.GetRequiredService<ILogger<NatsJetStreamKVNodeDiscovery>>();
                return new NatsJetStreamKVNodeDiscovery(
                    connection, 
                    logger,
                    bucketName: $"{subjectPrefix}_kv",
                    nodeTtl: TimeSpan.FromMinutes(5));
            });
        }
        else
        {
            // 使用 NATS Pub/Sub（轻量级）- 内存存储、无持久化
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

