using System.Diagnostics.CodeAnalysis;
using Catga;
using Catga.Cluster;
using Catga.Cluster.Discovery;
using Catga.Cluster.Metrics;
using Catga.Cluster.Remote;
using Catga.Cluster.Resilience;
using Catga.Cluster.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 集群服务扩展
/// </summary>
public static class ClusterServiceCollectionExtensions
{
    /// <summary>
    /// 添加集群支持（InMemory 发现）
    /// </summary>
    public static IServiceCollection AddCluster(
        this IServiceCollection services,
        Action<ClusterOptions>? configure = null)
    {
        var options = new ClusterOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<INodeDiscovery, InMemoryNodeDiscovery>();
        services.TryAddSingleton<IMessageRouter, RoundRobinRouter>();
        services.TryAddSingleton<ILoadReporter, SystemLoadReporter>();
        
        // 注册基础 HTTP 调用器
        services.AddSingleton<HttpRemoteInvoker>();
        
        // 根据配置选择是否启用故障转移
        if (options.EnableFailover)
        {
            services.AddSingleton<IRemoteInvoker>(sp =>
            {
                var httpInvoker = sp.GetRequiredService<HttpRemoteInvoker>();
                var discovery = sp.GetRequiredService<INodeDiscovery>();
                var logger = sp.GetRequiredService<ILogger<RetryRemoteInvoker>>();
                
                return new RetryRemoteInvoker(
                    httpInvoker,
                    discovery,
                    logger,
                    options.MaxRetries,
                    options.RetryDelay);
            });
        }
        else
        {
            services.AddSingleton<IRemoteInvoker>(sp => sp.GetRequiredService<HttpRemoteInvoker>());
        }
        
        // 添加 HTTP 客户端（用于远程调用）
        services.AddHttpClient("CatgaCluster", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        
        // 替换 ICatgaMediator 为 ClusterMediator
        services.Replace(ServiceDescriptor.Singleton<ICatgaMediator, ClusterMediator>());
        
        // 添加心跳后台服务
        services.AddHostedService<HeartbeatBackgroundService>();

        return services;
    }

    /// <summary>
    /// 使用自定义节点发现
    /// </summary>
    public static IServiceCollection UseNodeDiscovery<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, INodeDiscovery
    {
        services.Replace(ServiceDescriptor.Singleton<INodeDiscovery, T>());
        return services;
    }

    /// <summary>
    /// 使用自定义路由策略
    /// </summary>
    public static IServiceCollection UseMessageRouter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, IMessageRouter
    {
        services.Replace(ServiceDescriptor.Singleton<IMessageRouter, T>());
        return services;
    }

    /// <summary>
    /// 使用集群中间件（处理远程调用）
    /// </summary>
    public static IApplicationBuilder UseCluster(this IApplicationBuilder app)
    {
        app.UseMiddleware<ClusterInvokeMiddleware>();
        return app;
    }
}

/// <summary>
/// 心跳后台服务
/// </summary>
internal sealed class HeartbeatBackgroundService : BackgroundService
{
    private readonly INodeDiscovery _discovery;
    private readonly ILoadReporter _loadReporter;
    private readonly ClusterOptions _options;

    public HeartbeatBackgroundService(
        INodeDiscovery discovery, 
        ILoadReporter loadReporter,
        ClusterOptions options)
    {
        _discovery = discovery;
        _loadReporter = loadReporter;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 注册当前节点
        var node = new ClusterNode
        {
            NodeId = _options.NodeId,
            Endpoint = _options.Endpoint ?? $"http://localhost:5000",
            Status = NodeStatus.Online,
            Metadata = _options.Metadata
        };

        await _discovery.RegisterAsync(node, stoppingToken);

        try
        {
            // 定期发送心跳
            using var timer = new PeriodicTimer(_options.HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // 获取实际负载
                var load = await _loadReporter.GetCurrentLoadAsync(stoppingToken);
                await _discovery.HeartbeatAsync(_options.NodeId, load, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止，进行优雅下线
        }
        finally
        {
            // 优雅下线：注销节点（使用新的 CancellationToken，避免被取消）
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _discovery.UnregisterAsync(_options.NodeId, cts.Token);
            }
            catch
            {
                // 忽略注销失败
            }
        }
    }
}

