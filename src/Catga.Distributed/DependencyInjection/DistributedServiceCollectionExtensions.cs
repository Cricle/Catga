using Catga.Distributed.Nats;
using Catga.Distributed.Redis;
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
    public static IServiceCollection AddNatsCluster(
        this IServiceCollection services,
        string natsUrl,
        string nodeId,
        string endpoint,
        string subjectPrefix = "catga.nodes")
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

        // 注册节点发现（无锁）
        services.AddSingleton<INodeDiscovery>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var logger = sp.GetRequiredService<ILogger<NatsNodeDiscovery>>();
            return new NatsNodeDiscovery(connection, logger, subjectPrefix);
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
    public static IServiceCollection AddRedisCluster(
        this IServiceCollection services,
        string redisConnectionString,
        string nodeId,
        string endpoint,
        string keyPrefix = "catga:nodes:")
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

        // 注册节点发现（无锁）
        services.AddSingleton<INodeDiscovery>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<RedisNodeDiscovery>>();
            return new RedisNodeDiscovery(redis, logger, keyPrefix);
        });

        // 注册分布式 Mediator（无锁）
        services.AddSingleton<IDistributedMediator, DistributedMediator>();

        // 注册心跳后台服务（无锁）
        services.AddHostedService<HeartbeatBackgroundService>();

        return services;
    }
}

