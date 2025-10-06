using Catga.ServiceDiscovery;
using k8s;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.ServiceDiscovery.Kubernetes;

/// <summary>
/// Kubernetes 服务发现扩展方法
/// </summary>
public static class KubernetesServiceDiscoveryExtensions
{
    /// <summary>
    /// 添加 Kubernetes 服务发现（使用 K8s API）
    /// </summary>
    public static IServiceCollection AddKubernetesServiceDiscovery(
        this IServiceCollection services,
        Action<KubernetesServiceDiscoveryOptions>? configure = null)
    {
        var options = new KubernetesServiceDiscoveryOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<ILoadBalancer, RoundRobinLoadBalancer>();

        services.AddSingleton<IKubernetes>(sp =>
        {
            // 使用集群内配置或提供的 KubeConfig
            var config = options.KubeConfigPath != null
                ? KubernetesClientConfiguration.BuildConfigFromConfigFile(options.KubeConfigPath)
                : KubernetesClientConfiguration.InClusterConfig();

            return new k8s.Kubernetes(config);
        });

        services.AddSingleton<IServiceDiscovery>(sp =>
        {
            var kubernetes = sp.GetRequiredService<IKubernetes>();
            var loadBalancer = sp.GetRequiredService<ILoadBalancer>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<KubernetesServiceDiscovery>>();

            return new KubernetesServiceDiscovery(
                kubernetes,
                options.Namespace,
                loadBalancer,
                logger);
        });

        return services;
    }

    /// <summary>
    /// 添加 Kubernetes 服务发现（集群内模式）
    /// </summary>
    public static IServiceCollection AddKubernetesServiceDiscoveryInCluster(
        this IServiceCollection services,
        string @namespace = "default")
    {
        return services.AddKubernetesServiceDiscovery(options =>
        {
            options.Namespace = @namespace;
            options.KubeConfigPath = null; // 使用集群内配置
        });
    }
}

/// <summary>
/// Kubernetes 服务发现配置选项
/// </summary>
public class KubernetesServiceDiscoveryOptions
{
    /// <summary>
    /// 命名空间（默认：default）
    /// </summary>
    public string Namespace { get; set; } = "default";

    /// <summary>
    /// KubeConfig 文件路径（null 表示使用集群内配置）
    /// </summary>
    public string? KubeConfigPath { get; set; }
}

