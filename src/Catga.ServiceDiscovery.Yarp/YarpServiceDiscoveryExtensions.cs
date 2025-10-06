using Catga.ServiceDiscovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.ServiceDiscovery.Yarp;

/// <summary>
/// YARP 服务发现扩展方法
/// </summary>
public static class YarpServiceDiscoveryExtensions
{
    /// <summary>
    /// 添加 YARP 服务发现（从 YARP 配置读取服务信息）
    /// </summary>
    public static IServiceCollection AddYarpServiceDiscovery(this IServiceCollection services)
    {
        services.TryAddSingleton<ILoadBalancer, RoundRobinLoadBalancer>();
        services.AddSingleton<IServiceDiscovery, YarpServiceDiscovery>();
        return services;
    }
}

