using Catga.ServiceDiscovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.DependencyInjection;

/// <summary>
/// Service discovery extension methods
/// </summary>
public static class ServiceDiscoveryExtensions
{
    /// <summary>
    /// Add in-memory service discovery (for development/testing)
    /// </summary>
    public static IServiceCollection AddMemoryServiceDiscovery(this IServiceCollection services)
    {
        services.TryAddSingleton<ILoadBalancer, RoundRobinLoadBalancer>();
        services.TryAddSingleton<IServiceDiscovery, MemoryServiceDiscovery>();
        return services;
    }

    /// <summary>
    /// Add service registration (automatically register current service)
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
/// Service registration background service (automatic registration and heartbeat)
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
            // Register service
            await _serviceDiscovery.RegisterAsync(_options, cancellationToken);
            _serviceId = $"{_options.ServiceName}-{Guid.NewGuid():N}";

            _logger.LogInformation("Service registered: {ServiceName} at {Host}:{Port}",
                _options.ServiceName, _options.Host, _options.Port);

            // Start heartbeat timer
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

