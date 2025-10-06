using System.Runtime.CompilerServices;
using Catga.ServiceDiscovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Catga.ServiceDiscovery.Yarp;

/// <summary>
/// YARP 服务发现实现（从 YARP 配置读取服务信息）
/// </summary>
public class YarpServiceDiscovery : IServiceDiscovery, IDisposable
{
    private readonly IProxyConfigProvider _proxyConfigProvider;
    private readonly ILoadBalancer _loadBalancer;
    private readonly ILogger<YarpServiceDiscovery> _logger;
    private readonly Dictionary<string, List<ServiceInstance>> _serviceCache = new();
    private IDisposable? _changeToken;

    public YarpServiceDiscovery(
        IProxyConfigProvider proxyConfigProvider,
        ILoadBalancer? loadBalancer = null,
        ILogger<YarpServiceDiscovery>? logger = null)
    {
        _proxyConfigProvider = proxyConfigProvider;
        _loadBalancer = loadBalancer ?? new RoundRobinLoadBalancer();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<YarpServiceDiscovery>.Instance;

        // 初始化时加载配置
        LoadConfiguration();

        // 监听配置变化
        RegisterChangeCallback();
    }

    public Task RegisterAsync(ServiceRegistrationOptions options, CancellationToken cancellationToken = default)
    {
        // YARP 服务发现不支持动态注册（由 YARP 配置管理）
        _logger.LogWarning("YARP service discovery does not support dynamic registration");
        return Task.CompletedTask;
    }

    public Task DeregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        // YARP 服务发现不支持注销
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServiceInstance>> GetServiceInstancesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        if (_serviceCache.TryGetValue(serviceName, out var instances))
        {
            return Task.FromResult<IReadOnlyList<ServiceInstance>>(instances);
        }

        _logger.LogWarning("Service {ServiceName} not found in YARP configuration", serviceName);
        return Task.FromResult<IReadOnlyList<ServiceInstance>>(Array.Empty<ServiceInstance>());
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
        // YARP 不需要心跳（由 YARP 健康检查管理）
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ServiceChangeEvent> WatchServiceAsync(
        string serviceName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // YARP 配置变化监听（简化实现）
        _logger.LogInformation("Started watching service: {ServiceName}", serviceName);

        // 返回当前状态
        var instances = await GetServiceInstancesAsync(serviceName, cancellationToken);
        foreach (var instance in instances)
        {
            yield return new ServiceChangeEvent(ServiceChangeType.Registered, instance);
        }

        // TODO: 实现配置变化时的实时通知
        await Task.CompletedTask;
    }

    private void LoadConfiguration()
    {
        var config = _proxyConfigProvider.GetConfig();
        _serviceCache.Clear();

        foreach (var cluster in config.Clusters)
        {
            var serviceName = cluster.ClusterId;
            var instances = new List<ServiceInstance>();

            if (cluster.Destinations != null)
            {
                foreach (var (destinationId, destination) in cluster.Destinations)
                {
                    if (destination.Address != null && Uri.TryCreate(destination.Address, UriKind.Absolute, out var uri))
                    {
                        var metadata = destination.Metadata != null
                            ? new Dictionary<string, string>(destination.Metadata)
                            : null;

                        var instance = new ServiceInstance(
                            destinationId,
                            serviceName,
                            uri.Host,
                            uri.Port)
                        {
                            Metadata = metadata,
                            IsHealthy = true
                        };

                        instances.Add(instance);
                    }
                }
            }

            if (instances.Count > 0)
            {
                _serviceCache[serviceName] = instances;
                _logger.LogInformation("Loaded {Count} instances for service {ServiceName} from YARP config",
                    instances.Count, serviceName);
            }
        }
    }

    private void RegisterChangeCallback()
    {
        var config = _proxyConfigProvider.GetConfig();
        _changeToken = ChangeToken.OnChange(
            () => config.ChangeToken,
            () =>
            {
                _logger.LogInformation("YARP configuration changed, reloading...");
                LoadConfiguration();
            });
    }

    public void Dispose()
    {
        _changeToken?.Dispose();
    }
}

