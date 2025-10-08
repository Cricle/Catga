using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Catga.ServiceDiscovery;

/// <summary>
/// In-memory service discovery implementation (for testing and standalone deployments)
/// </summary>
public class MemoryServiceDiscovery : IServiceDiscovery
{
    private readonly ConcurrentDictionary<string, ServiceInstance> _services = new();
    private readonly ConcurrentDictionary<string, Channel<ServiceChangeEvent>> _watchers = new();
    private readonly ILoadBalancer _loadBalancer;
    private readonly ILogger<MemoryServiceDiscovery> _logger;

    public MemoryServiceDiscovery(
        ILoadBalancer? loadBalancer = null,
        ILogger<MemoryServiceDiscovery>? logger = null)
    {
        _loadBalancer = loadBalancer ?? new RoundRobinLoadBalancer();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MemoryServiceDiscovery>.Instance;
    }

    public Task RegisterAsync(ServiceRegistrationOptions options, CancellationToken cancellationToken = default)
    {
        var serviceId = $"{options.ServiceName}-{Guid.NewGuid():N}";
        var instance = new ServiceInstance(
            serviceId,
            options.ServiceName,
            options.Host,
            options.Port,
            options.Metadata)
        {
            IsHealthy = true,
            LastHeartbeat = DateTime.UtcNow
        };

        _services[serviceId] = instance;
        _logger.LogInformation("Service registered: {ServiceId} ({ServiceName}) at {Address}",
            serviceId, options.ServiceName, instance.Address);

        // Notify watchers
        NotifyWatchers(options.ServiceName, new ServiceChangeEvent(ServiceChangeType.Registered, instance));

        return Task.CompletedTask;
    }

    public Task DeregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (_services.TryRemove(serviceId, out var instance))
        {
            _logger.LogInformation("Service deregistered: {ServiceId} ({ServiceName})",
                serviceId, instance.ServiceName);

            // Notify watchers
            NotifyWatchers(instance.ServiceName, new ServiceChangeEvent(ServiceChangeType.Deregistered, instance));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServiceInstance>> GetServiceInstancesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        // 🔥 优化: 避免 ToList() 分配，使用数组代替
        var instances = _services.Values.Where(s => s.ServiceName == serviceName && s.IsHealthy).ToArray();
        return Task.FromResult<IReadOnlyList<ServiceInstance>>(instances);
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
        if (_services.TryGetValue(serviceId, out var instance))
        {
            var updated = instance with { LastHeartbeat = DateTime.UtcNow, IsHealthy = true };
            _services[serviceId] = updated;
            _logger.LogTrace("Heartbeat received for service: {ServiceId}", serviceId);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ServiceChangeEvent> WatchServiceAsync(
        string serviceName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<ServiceChangeEvent>();
        _watchers[serviceName] = channel;

        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return change;
            }
        }
        finally
        {
            _watchers.TryRemove(serviceName, out _);
        }
    }

    private void NotifyWatchers(string serviceName, ServiceChangeEvent change)
    {
        if (_watchers.TryGetValue(serviceName, out var channel))
        {
            channel.Writer.TryWrite(change);
        }
    }

    /// <summary>
    /// Get all services (for monitoring)
    /// </summary>
    public IReadOnlyDictionary<string, ServiceInstance> GetAllServices()
    {
        return _services;
    }
}

