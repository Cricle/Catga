using k8s;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.ServiceDiscovery.Kubernetes;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Kubernetes service discovery
    /// </summary>
    public static IServiceCollection AddKubernetesServiceDiscovery(
        this IServiceCollection services,
        Action<KubernetesClientConfiguration>? configureClient = null)
    {
        services.AddSingleton<IKubernetes>(sp =>
        {
            var config = KubernetesClientConfiguration.InClusterConfig();
            configureClient?.Invoke(config);
            return new k8s.Kubernetes(config);
        });

        services.AddSingleton<KubernetesServiceDiscovery>();

        return services;
    }

    /// <summary>
    /// Add Kubernetes service discovery with custom config
    /// </summary>
    public static IServiceCollection AddKubernetesServiceDiscovery(
        this IServiceCollection services,
        string kubeConfigPath)
    {
        services.AddSingleton<IKubernetes>(sp =>
        {
            var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeConfigPath);
            return new k8s.Kubernetes(config);
        });

        services.AddSingleton<KubernetesServiceDiscovery>();

        return services;
    }
}

