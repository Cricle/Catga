using System.Runtime.CompilerServices;
using Catga.ServiceDiscovery;
using Consul;
using Microsoft.Extensions.Logging;

namespace Catga.ServiceDiscovery.Consul;

/// <summary>
/// Consul 服务发现实现
/// </summary>
public class ConsulServiceDiscovery : IServiceDiscovery
{
    private readonly IConsulClient _consul;
    private readonly ILoadBalancer _loadBalancer;
    private readonly ILogger<ConsulServiceDiscovery> _logger;

    public ConsulServiceDiscovery(
        IConsulClient consul,
        ILoadBalancer? loadBalancer = null,
        ILogger<ConsulServiceDiscovery>? logger = null)
    {
        _consul = consul;
        _loadBalancer = loadBalancer ?? new RoundRobinLoadBalancer();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsulServiceDiscovery>.Instance;
    }

    public async Task RegisterAsync(ServiceRegistrationOptions options, CancellationToken cancellationToken = default)
    {
        var serviceId = $"{options.ServiceName}-{Guid.NewGuid():N}";

        var registration = new AgentServiceRegistration
        {
            ID = serviceId,
            Name = options.ServiceName,
            Address = options.Host,
            Port = options.Port,
            Tags = options.Metadata?.Select(kv => $"{kv.Key}={kv.Value}").ToArray(),
            Check = options.HealthCheckUrl != null ? new AgentServiceCheck
            {
                HTTP = options.HealthCheckUrl,
                Interval = options.HealthCheckInterval,
                Timeout = options.HealthCheckTimeout,
                DeregisterCriticalServiceAfter = TimeSpan.FromMinutes(1)
            } : null
        };

        await _consul.Agent.ServiceRegister(registration, cancellationToken);

        _logger.LogInformation("Service registered in Consul: {ServiceId} ({ServiceName}) at {Address}:{Port}",
            serviceId, options.ServiceName, options.Host, options.Port);
    }

    public async Task DeregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        await _consul.Agent.ServiceDeregister(serviceId, cancellationToken);
        _logger.LogInformation("Service deregistered from Consul: {ServiceId}", serviceId);
    }

    public async Task<IReadOnlyList<ServiceInstance>> GetServiceInstancesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var services = await _consul.Health.Service(serviceName, null, true, cancellationToken);

        var instances = services.Response
            .Select(entry => new ServiceInstance(
                entry.Service.ID,
                entry.Service.Service,
                entry.Service.Address,
                entry.Service.Port,
                ParseMetadata(entry.Service.Tags))
            {
                IsHealthy = entry.Checks.All(c => c.Status == HealthStatus.Passing)
            })
            .ToList();

        return instances;
    }

    public async Task<ServiceInstance?> GetServiceInstanceAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var instances = await GetServiceInstancesAsync(serviceName, cancellationToken);
        return _loadBalancer.SelectInstance(instances);
    }

    public async Task SendHeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        // Consul 使用健康检查，不需要手动心跳
        // 但我们可以通过 TTL 检查来实现
        await _consul.Agent.PassTTL($"service:{serviceId}", null, cancellationToken);
        _logger.LogTrace("Heartbeat sent to Consul for service: {ServiceId}", serviceId);
    }

    public async IAsyncEnumerable<ServiceChangeEvent> WatchServiceAsync(
        string serviceName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ulong lastIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            QueryResult<ServiceEntry[]>? result = null;
            
            try
            {
                var queryOptions = new QueryOptions { WaitIndex = lastIndex, WaitTime = TimeSpan.FromSeconds(30) };
                result = await _consul.Health.Service(serviceName, null, false, queryOptions, cancellationToken);

                if (result.LastIndex > lastIndex)
                {
                    lastIndex = result.LastIndex;
                }
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error watching Consul service: {ServiceName}", serviceName);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                continue;
            }

            // 在 try-catch 外 yield return
            if (result != null)
            {
                foreach (var entry in result.Response)
                {
                    var instance = new ServiceInstance(
                        entry.Service.ID,
                        entry.Service.Service,
                        entry.Service.Address,
                        entry.Service.Port,
                        ParseMetadata(entry.Service.Tags))
                    {
                        IsHealthy = entry.Checks.All(c => c.Status == HealthStatus.Passing)
                    };

                    yield return new ServiceChangeEvent(ServiceChangeType.Registered, instance);
                }
            }

            await Task.Delay(1000, cancellationToken);
        }
    }

    private static Dictionary<string, string>? ParseMetadata(string[]? tags)
    {
        if (tags == null || tags.Length == 0)
            return null;

        var metadata = new Dictionary<string, string>();
        foreach (var tag in tags)
        {
            var parts = tag.Split('=', 2);
            if (parts.Length == 2)
            {
                metadata[parts[0]] = parts[1];
            }
        }

        return metadata.Count > 0 ? metadata : null;
    }
}

