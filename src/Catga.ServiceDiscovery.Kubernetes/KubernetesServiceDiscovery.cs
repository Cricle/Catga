using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace Catga.ServiceDiscovery.Kubernetes;

/// <summary>
/// Kubernetes-based service discovery using K8s API
/// </summary>
public class KubernetesServiceDiscovery : IDisposable
{
    private readonly IKubernetes _client;
    private readonly ILogger<KubernetesServiceDiscovery> _logger;
    private readonly string _namespace;

    public KubernetesServiceDiscovery(
        IKubernetes client,
        ILogger<KubernetesServiceDiscovery> logger,
        string? namespace_ = null)
    {
        _client = client;
        _logger = logger;
        _namespace = namespace_ ?? "default";
    }

    /// <summary>
    /// Get service endpoint by name (uses K8s DNS)
    /// </summary>
    public async Task<string> GetServiceEndpointAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        try
        {
            var service = await _client.CoreV1.ReadNamespacedServiceAsync(serviceName, _namespace, cancellationToken: cancellationToken);
            
            // Return K8s DNS name
            return $"{serviceName}.{_namespace}.svc.cluster.local:{service.Spec.Ports[0].Port}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get service endpoint for {ServiceName}", serviceName);
            throw;
        }
    }

    /// <summary>
    /// List all services in namespace
    /// </summary>
    public async Task<IReadOnlyList<string>> ListServicesAsync(CancellationToken cancellationToken = default)
    {
        var services = await _client.CoreV1.ListNamespacedServiceAsync(_namespace, cancellationToken: cancellationToken);
        return services.Items.Select(s => s.Metadata.Name).ToList();
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}

