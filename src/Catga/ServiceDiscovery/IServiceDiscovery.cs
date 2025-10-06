namespace Catga.ServiceDiscovery;

/// <summary>
/// 服务实例信息
/// </summary>
public record ServiceInstance(
    string ServiceId,
    string ServiceName,
    string Host,
    int Port,
    Dictionary<string, string>? Metadata = null)
{
    /// <summary>
    /// 服务地址（http://host:port 或 nats://host:port）
    /// </summary>
    public string Address => $"{Host}:{Port}";

    /// <summary>
    /// 健康状态
    /// </summary>
    public bool IsHealthy { get; init; } = true;

    /// <summary>
    /// 最后心跳时间
    /// </summary>
    public DateTime LastHeartbeat { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 服务注册选项
/// </summary>
public record ServiceRegistrationOptions
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// 主机地址
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// 端口
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// 服务元数据
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// 健康检查 URL（可选）
    /// </summary>
    public string? HealthCheckUrl { get; init; }

    /// <summary>
    /// 健康检查间隔（默认 10 秒）
    /// </summary>
    public TimeSpan HealthCheckInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 健康检查超时（默认 5 秒）
    /// </summary>
    public TimeSpan HealthCheckTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 自动注销（默认启用）
    /// </summary>
    public bool DeregisterOnShutdown { get; init; } = true;
}

/// <summary>
/// 服务发现抽象接口（平台无关）
/// </summary>
public interface IServiceDiscovery
{
    /// <summary>
    /// 注册服务实例
    /// </summary>
    Task RegisterAsync(ServiceRegistrationOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销服务实例
    /// </summary>
    Task DeregisterAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取服务的所有实例
    /// </summary>
    Task<IReadOnlyList<ServiceInstance>> GetServiceInstancesAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取单个健康的服务实例（负载均衡）
    /// </summary>
    Task<ServiceInstance?> GetServiceInstanceAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送心跳（保持服务活跃）
    /// </summary>
    Task SendHeartbeatAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 监听服务变化
    /// </summary>
    IAsyncEnumerable<ServiceChangeEvent> WatchServiceAsync(string serviceName, CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务变化事件
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
/// 服务变化类型
/// </summary>
public enum ServiceChangeType
{
    /// <summary>
    /// 服务注册
    /// </summary>
    Registered,

    /// <summary>
    /// 服务注销
    /// </summary>
    Deregistered,

    /// <summary>
    /// 服务状态变化
    /// </summary>
    HealthChanged
}

/// <summary>
/// 负载均衡策略
/// </summary>
public interface ILoadBalancer
{
    /// <summary>
    /// 从多个实例中选择一个
    /// </summary>
    ServiceInstance? SelectInstance(IReadOnlyList<ServiceInstance> instances);
}

/// <summary>
/// 轮询负载均衡器
/// </summary>
public class RoundRobinLoadBalancer : ILoadBalancer
{
    private int _currentIndex = 0;

    public ServiceInstance? SelectInstance(IReadOnlyList<ServiceInstance> instances)
    {
        if (instances.Count == 0)
            return null;

        // 线程安全的递增
        var index = Interlocked.Increment(ref _currentIndex) % instances.Count;
        return instances[index];
    }
}

/// <summary>
/// 随机负载均衡器
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

