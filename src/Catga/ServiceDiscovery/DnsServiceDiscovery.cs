using System.Net;
using Microsoft.Extensions.Logging;

namespace Catga.ServiceDiscovery;

/// <summary>
/// DNS 服务发现实现（适用于 Kubernetes 等环境）
/// </summary>
public class DnsServiceDiscovery : IServiceDiscovery
{
    private readonly ILoadBalancer _loadBalancer;
    private readonly ILogger<DnsServiceDiscovery> _logger;
    private readonly Dictionary<string, (string DnsName, int Port)> _serviceMapping = new();

    public DnsServiceDiscovery(
        ILoadBalancer? loadBalancer = null,
        ILogger<DnsServiceDiscovery>? logger = null)
    {
        _loadBalancer = loadBalancer ?? new RoundRobinLoadBalancer();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DnsServiceDiscovery>.Instance;
    }

    /// <summary>
    /// 配置服务的 DNS 映射
    /// </summary>
    public void ConfigureService(string serviceName, string dnsName, int port)
    {
        _serviceMapping[serviceName] = (dnsName, port);
        _logger.LogInformation("Service DNS mapping configured: {ServiceName} -> {DnsName}:{Port}",
            serviceName, dnsName, port);
    }

    public Task RegisterAsync(ServiceRegistrationOptions options, CancellationToken cancellationToken = default)
    {
        // DNS 服务发现不需要注册（由 DNS 系统管理）
        _logger.LogWarning("DNS service discovery does not support registration");
        return Task.CompletedTask;
    }

    public Task DeregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        // DNS 服务发现不需要注销
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ServiceInstance>> GetServiceInstancesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        if (!_serviceMapping.TryGetValue(serviceName, out var mapping))
        {
            _logger.LogWarning("Service {ServiceName} not configured in DNS mapping", serviceName);
            return Array.Empty<ServiceInstance>();
        }

        try
        {
            // 解析 DNS A 记录
            var addresses = await Dns.GetHostAddressesAsync(mapping.DnsName, cancellationToken);

            var instances = addresses
                .Select((addr, index) => new ServiceInstance(
                    $"{serviceName}-{index}",
                    serviceName,
                    addr.ToString(),
                    mapping.Port)
                {
                    IsHealthy = true
                })
                .ToList();

            _logger.LogDebug("Resolved {Count} instances for service {ServiceName} from DNS",
                instances.Count, serviceName);

            return instances;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve DNS for service {ServiceName}", serviceName);
            return Array.Empty<ServiceInstance>();
        }
    }

    public async Task<ServiceInstance?> GetServiceInstanceAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var instances = await GetServiceInstancesAsync(serviceName, cancellationToken);
        return _loadBalancer.SelectInstance(instances);
    }

    public Task SendHeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        // DNS 服务发现不需要心跳
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ServiceChangeEvent> WatchServiceAsync(
        string serviceName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // DNS 服务发现不支持实时监听（可以通过轮询实现）
        _logger.LogWarning("DNS service discovery does not support real-time watching");
        yield break;
        await Task.CompletedTask; // 避免编译器警告
    }
}

