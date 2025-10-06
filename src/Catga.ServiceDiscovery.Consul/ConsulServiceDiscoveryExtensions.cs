using Catga.ServiceDiscovery;
using Consul;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.ServiceDiscovery.Consul;

/// <summary>
/// Consul 服务发现扩展方法
/// </summary>
public static class ConsulServiceDiscoveryExtensions
{
    /// <summary>
    /// 添加 Consul 服务发现
    /// </summary>
    public static IServiceCollection AddConsulServiceDiscovery(
        this IServiceCollection services,
        Action<ConsulServiceDiscoveryOptions> configure)
    {
        var options = new ConsulServiceDiscoveryOptions();
        configure(options);

        services.TryAddSingleton<ILoadBalancer, RoundRobinLoadBalancer>();

        services.AddSingleton<IConsulClient>(sp => new ConsulClient(config =>
        {
            config.Address = new Uri(options.ConsulAddress);
            if (!string.IsNullOrEmpty(options.Token))
            {
                config.Token = options.Token;
            }
            if (!string.IsNullOrEmpty(options.Datacenter))
            {
                config.Datacenter = options.Datacenter;
            }
        }));

        services.AddSingleton<IServiceDiscovery, ConsulServiceDiscovery>();

        return services;
    }
}

/// <summary>
/// Consul 服务发现配置选项
/// </summary>
public class ConsulServiceDiscoveryOptions
{
    /// <summary>
    /// Consul 地址（默认：http://localhost:8500）
    /// </summary>
    public string ConsulAddress { get; set; } = "http://localhost:8500";

    /// <summary>
    /// Consul Token（可选）
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Consul 数据中心（可选）
    /// </summary>
    public string? Datacenter { get; set; }
}

