using Catga.ServiceDiscovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.DependencyInjection;

/// <summary>
/// 服务发现扩展方法
/// </summary>
public static class ServiceDiscoveryExtensions
{
    /// <summary>
    /// 添加内存服务发现（单机部署）
    /// </summary>
    public static IServiceCollection AddMemoryServiceDiscovery(this IServiceCollection services)
    {
        services.TryAddSingleton<ILoadBalancer, RoundRobinLoadBalancer>();
        services.TryAddSingleton<IServiceDiscovery, MemoryServiceDiscovery>();
        return services;
    }

    /// <summary>
    /// 添加 DNS 服务发现（Kubernetes）
    /// </summary>
    public static IServiceCollection AddDnsServiceDiscovery(
        this IServiceCollection services,
        Action<DnsServiceDiscoveryOptions>? configure = null)
    {
        services.TryAddSingleton<ILoadBalancer, RoundRobinLoadBalancer>();

        services.AddSingleton<IServiceDiscovery>(sp =>
        {
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<DnsServiceDiscovery>>();
            var loadBalancer = sp.GetRequiredService<ILoadBalancer>();
            var discovery = new DnsServiceDiscovery(loadBalancer, logger);

            if (configure != null)
            {
                var options = new DnsServiceDiscoveryOptions();
                configure(options);

                foreach (var (serviceName, mapping) in options.ServiceMappings)
                {
                    discovery.ConfigureService(serviceName, mapping.DnsName, mapping.Port);
                }
            }

            return discovery;
        });

        return services;
    }

    /// <summary>
    /// 添加服务注册（自动注册当前服务）
    /// </summary>
    public static IServiceCollection AddServiceRegistration(
        this IServiceCollection services,
        ServiceRegistrationOptions options)
    {
        services.AddSingleton(options);
        services.AddHostedService<ServiceRegistrationHostedService>();
        return services;
    }
}

/// <summary>
/// DNS 服务发现配置选项
/// </summary>
public class DnsServiceDiscoveryOptions
{
    internal Dictionary<string, (string DnsName, int Port)> ServiceMappings { get; } = new();

    /// <summary>
    /// 配置服务 DNS 映射
    /// </summary>
    public DnsServiceDiscoveryOptions MapService(string serviceName, string dnsName, int port)
    {
        ServiceMappings[serviceName] = (dnsName, port);
        return this;
    }
}

/// <summary>
/// 服务注册后台服务（自动注册和心跳）
/// </summary>
internal class ServiceRegistrationHostedService : IHostedService
{
    private readonly IServiceDiscovery _serviceDiscovery;
    private readonly ServiceRegistrationOptions _options;
    private readonly Microsoft.Extensions.Logging.ILogger<ServiceRegistrationHostedService> _logger;
    private string? _serviceId;
    private Timer? _heartbeatTimer;

    public ServiceRegistrationHostedService(
        IServiceDiscovery serviceDiscovery,
        ServiceRegistrationOptions options,
        Microsoft.Extensions.Logging.ILogger<ServiceRegistrationHostedService> logger)
    {
        _serviceDiscovery = serviceDiscovery;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 注册服务
            await _serviceDiscovery.RegisterAsync(_options, cancellationToken);
            _serviceId = $"{_options.ServiceName}-{Guid.NewGuid():N}";

            _logger.LogInformation("Service registered: {ServiceName} at {Host}:{Port}",
                _options.ServiceName, _options.Host, _options.Port);

            // 启动心跳定时器
            _heartbeatTimer = new Timer(
                SendHeartbeat,
                null,
                _options.HealthCheckInterval,
                _options.HealthCheckInterval);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register service");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer?.Dispose();

        if (_options.DeregisterOnShutdown && _serviceId != null)
        {
            try
            {
                await _serviceDiscovery.DeregisterAsync(_serviceId, cancellationToken);
                _logger.LogInformation("Service deregistered: {ServiceId}", _serviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deregister service");
            }
        }
    }

    private async void SendHeartbeat(object? state)
    {
        if (_serviceId == null) return;

        try
        {
            await _serviceDiscovery.SendHeartbeatAsync(_serviceId);
            _logger.LogTrace("Heartbeat sent for service: {ServiceId}", _serviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send heartbeat");
        }
    }
}

