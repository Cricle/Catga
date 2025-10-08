namespace Catga.ServiceDiscovery;

/// <summary>
/// Service instance information
/// </summary>
public record ServiceInstance(
    string ServiceId,
    string ServiceName,
    string Host,
    int Port,
    Dictionary<string, string>? Metadata = null)
{
    public string Address => $"{Host}:{Port}";
    public bool IsHealthy { get; init; } = true;
    public DateTime LastHeartbeat { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Service registration options
/// </summary>
public record ServiceRegistrationOptions
{
    public required string ServiceName { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    public string? HealthCheckUrl { get; init; }
    public TimeSpan HealthCheckInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan HealthCheckTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public bool DeregisterOnShutdown { get; init; } = true;
}

/// <summary>
/// Service discovery interface (platform-agnostic)
/// </summary>
public interface IServiceDiscovery
{
    public Task RegisterAsync(ServiceRegistrationOptions options, CancellationToken cancellationToken = default);
    public Task DeregisterAsync(string serviceId, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<ServiceInstance>> GetServiceInstancesAsync(string serviceName, CancellationToken cancellationToken = default);
    public Task<ServiceInstance?> GetServiceInstanceAsync(string serviceName, CancellationToken cancellationToken = default);
    public Task SendHeartbeatAsync(string serviceId, CancellationToken cancellationToken = default);
    public IAsyncEnumerable<ServiceChangeEvent> WatchServiceAsync(string serviceName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service change event
/// </summary>
public record ServiceChangeEvent(
    ServiceChangeType ChangeType,
    ServiceInstance Instance,
    DateTime Timestamp)
{
    public ServiceChangeEvent(ServiceChangeType changeType, ServiceInstance instance)
        : this(changeType, instance, DateTime.UtcNow)
    {
    }
}

/// <summary>
/// Service change type
/// </summary>
public enum ServiceChangeType
{
    Registered,
    Deregistered,
    HealthChanged
}

/// <summary>
/// Load balancer strategy
/// </summary>
public interface ILoadBalancer
{
    public ServiceInstance? SelectInstance(IReadOnlyList<ServiceInstance> instances);
}

/// <summary>
/// Round-robin load balancer
/// </summary>
public class RoundRobinLoadBalancer : ILoadBalancer
{
    private int _currentIndex = 0;

    public ServiceInstance? SelectInstance(IReadOnlyList<ServiceInstance> instances)
    {
        if (instances.Count == 0)
            return null;

        // Thread-safe increment
        var index = Interlocked.Increment(ref _currentIndex) % instances.Count;
        return instances[index];
    }
}

/// <summary>
/// Random load balancer
/// </summary>
public class RandomLoadBalancer : ILoadBalancer
{
    private readonly Random _random = new();

    public ServiceInstance? SelectInstance(IReadOnlyList<ServiceInstance> instances)
    {
        if (instances.Count == 0)
            return null;

        var index = _random.Next(instances.Count);
        return instances[index];
    }
}

