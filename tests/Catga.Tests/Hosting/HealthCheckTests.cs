using Catga.Hosting;
using Catga.Transport;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// 健康检查单元测试
/// Requirements: 7.2, 7.3, 7.4, 7.5, 7.6
/// </summary>
public class HealthCheckTests
{
    #region TransportHealthCheck Tests

    [Fact]
    public async Task TransportHealthCheck_ReturnsHealthy_WhenTransportIsHealthy()
    {
        // Arrange
        var transport = Substitute.For<IMessageTransport, IHealthCheckable>();
        transport.Name.Returns("TestTransport");
        
        var healthCheckable = (IHealthCheckable)transport;
        healthCheckable.IsHealthy.Returns(true);
        healthCheckable.HealthStatus.Returns("Connected");
        healthCheckable.LastHealthCheck.Returns(DateTimeOffset.UtcNow);

        var healthCheck = new TransportHealthCheck(transport);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("connected", result.Description ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Data.ContainsKey("transport_name"));
        Assert.Equal("TestTransport", result.Data["transport_name"]);
    }

    [Fact]
    public async Task TransportHealthCheck_ReturnsUnhealthy_WhenTransportIsUnhealthy()
    {
        // Arrange
        var transport = Substitute.For<IMessageTransport, IHealthCheckable>();
        transport.Name.Returns("TestTransport");
        
        var healthCheckable = (IHealthCheckable)transport;
        healthCheckable.IsHealthy.Returns(false);
        healthCheckable.HealthStatus.Returns("Disconnected");
        healthCheckable.LastHealthCheck.Returns(DateTimeOffset.UtcNow);

        var healthCheck = new TransportHealthCheck(transport);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("disconnected", result.Description ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransportHealthCheck_ReturnsHealthy_WhenTransportDoesNotSupportHealthCheck()
    {
        // Arrange
        var transport = Substitute.For<IMessageTransport>();
        transport.Name.Returns("TestTransport");

        var healthCheck = new TransportHealthCheck(transport);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("does not support", result.Description ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Data.ContainsKey("supports_health_check"));
        Assert.False((bool)result.Data["supports_health_check"]);
    }

    [Fact]
    public async Task TransportHealthCheck_IncludesLastCheckTime_WhenAvailable()
    {
        // Arrange
        var lastCheck = DateTimeOffset.UtcNow.AddMinutes(-1);
        var transport = Substitute.For<IMessageTransport, IHealthCheckable>();
        transport.Name.Returns("TestTransport");
        
        var healthCheckable = (IHealthCheckable)transport;
        healthCheckable.IsHealthy.Returns(true);
        healthCheckable.HealthStatus.Returns("Connected");
        healthCheckable.LastHealthCheck.Returns(lastCheck);

        var healthCheck = new TransportHealthCheck(transport);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.True(result.Data.ContainsKey("last_health_check"));
        Assert.True(result.Data.ContainsKey("seconds_since_last_check"));
        Assert.Equal(lastCheck, result.Data["last_health_check"]);
    }

    [Fact]
    public async Task TransportHealthCheck_HandlesException_ReturnsUnhealthy()
    {
        // Arrange
        var transport = Substitute.For<IMessageTransport, IHealthCheckable>();
        transport.Name.Returns("TestTransport");
        
        var healthCheckable = (IHealthCheckable)transport;
        healthCheckable.IsHealthy.Returns(_ => throw new InvalidOperationException("Test exception"));

        var healthCheck = new TransportHealthCheck(transport);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
        Assert.Contains("error", result.Description ?? "", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region PersistenceHealthCheck Tests

    [Fact]
    public async Task PersistenceHealthCheck_ReturnsHealthy_WhenAllComponentsHealthy()
    {
        // Arrange
        var components = new List<IHealthCheckable>();
        for (int i = 0; i < 3; i++)
        {
            var component = Substitute.For<IHealthCheckable>();
            component.IsHealthy.Returns(true);
            component.HealthStatus.Returns("OK");
            component.LastHealthCheck.Returns(DateTimeOffset.UtcNow);
            components.Add(component);
        }

        var healthCheck = new PersistenceHealthCheck(components);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.True(result.Data.ContainsKey("total_components"));
        Assert.Equal(3, result.Data["total_components"]);
        Assert.Equal(3, result.Data["healthy_count"]);
        Assert.Equal(0, result.Data["unhealthy_count"]);
    }

    [Fact]
    public async Task PersistenceHealthCheck_ReturnsUnhealthy_WhenSomeComponentsUnhealthy()
    {
        // Arrange
        var components = new List<IHealthCheckable>();
        
        // 2 healthy components
        for (int i = 0; i < 2; i++)
        {
            var component = Substitute.For<IHealthCheckable>();
            component.IsHealthy.Returns(true);
            component.HealthStatus.Returns("OK");
            components.Add(component);
        }
        
        // 1 unhealthy component
        var unhealthyComponent = Substitute.For<IHealthCheckable>();
        unhealthyComponent.IsHealthy.Returns(false);
        unhealthyComponent.HealthStatus.Returns("Failed");
        components.Add(unhealthyComponent);

        var healthCheck = new PersistenceHealthCheck(components);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(3, result.Data["total_components"]);
        Assert.Equal(2, result.Data["healthy_count"]);
        Assert.Equal(1, result.Data["unhealthy_count"]);
        Assert.True(result.Data.ContainsKey("unhealthy_components"));
    }

    [Fact]
    public async Task PersistenceHealthCheck_ReturnsHealthy_WhenNoComponents()
    {
        // Arrange
        var components = new List<IHealthCheckable>();
        var healthCheck = new PersistenceHealthCheck(components);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("No persistence components", result.Description ?? "");
        Assert.Equal(0, result.Data["component_count"]);
    }

    [Fact]
    public async Task PersistenceHealthCheck_IncludesComponentDetails()
    {
        // Arrange
        var component = Substitute.For<IHealthCheckable>();
        component.IsHealthy.Returns(true);
        component.HealthStatus.Returns("OK");
        component.LastHealthCheck.Returns(DateTimeOffset.UtcNow);

        var healthCheck = new PersistenceHealthCheck(new[] { component });
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        var componentName = component.GetType().Name;
        Assert.True(result.Data.ContainsKey($"{componentName}_is_healthy"));
        Assert.True(result.Data.ContainsKey($"{componentName}_status"));
        Assert.True(result.Data.ContainsKey($"{componentName}_last_check"));
    }

    [Fact]
    public async Task PersistenceHealthCheck_HandlesException_ReturnsUnhealthy()
    {
        // Arrange
        var component = Substitute.For<IHealthCheckable>();
        component.IsHealthy.Returns(_ => throw new InvalidOperationException("Test exception"));

        var healthCheck = new PersistenceHealthCheck(new[] { component });
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
        Assert.Contains("error", result.Description ?? "", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region RecoveryHealthCheck Tests

    [Fact]
    public async Task RecoveryHealthCheck_ReturnsHealthy_WhenAllComponentsHealthy()
    {
        // Arrange
        var components = new List<IRecoverableComponent>();
        for (int i = 0; i < 3; i++)
        {
            var component = Substitute.For<IRecoverableComponent>();
            component.ComponentName.Returns($"Component{i}");
            component.IsHealthy.Returns(true);
            component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            components.Add(component);
        }

        var healthCheck = new RecoveryHealthCheck(components);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(3, result.Data["total_components"]);
        Assert.Equal(3, result.Data["healthy_count"]);
        Assert.Equal(0, result.Data["unhealthy_count"]);
    }

    [Fact]
    public async Task RecoveryHealthCheck_ReturnsDegraded_WhenSomeComponentsUnhealthy()
    {
        // Arrange
        var components = new List<IRecoverableComponent>();
        
        // 2 healthy components
        for (int i = 0; i < 2; i++)
        {
            var component = Substitute.For<IRecoverableComponent>();
            component.ComponentName.Returns($"HealthyComponent{i}");
            component.IsHealthy.Returns(true);
            components.Add(component);
        }
        
        // 1 unhealthy component
        var unhealthyComponent = Substitute.For<IRecoverableComponent>();
        unhealthyComponent.ComponentName.Returns("UnhealthyComponent");
        unhealthyComponent.IsHealthy.Returns(false);
        components.Add(unhealthyComponent);

        var healthCheck = new RecoveryHealthCheck(components);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(3, result.Data["total_components"]);
        Assert.Equal(2, result.Data["healthy_count"]);
        Assert.Equal(1, result.Data["unhealthy_count"]);
        Assert.True(result.Data.ContainsKey("unhealthy_components"));
    }

    [Fact]
    public async Task RecoveryHealthCheck_ReturnsHealthy_WhenNoComponents()
    {
        // Arrange
        var components = new List<IRecoverableComponent>();
        var healthCheck = new RecoveryHealthCheck(components);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("No recoverable components", result.Description ?? "");
    }

    [Fact]
    public async Task RecoveryHealthCheck_IncludesRecoveryServiceStatus_WhenProvided()
    {
        // Arrange
        var components = new List<IRecoverableComponent>();
        var component = Substitute.For<IRecoverableComponent>();
        component.ComponentName.Returns("TestComponent");
        component.IsHealthy.Returns(true);
        components.Add(component);

        // Create a real RecoveryHostedService instance
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<RecoveryHostedService>>();
        var recoveryService = new RecoveryHostedService(logger, components, new RecoveryOptions());

        var healthCheck = new RecoveryHealthCheck(components, recoveryService);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.True(result.Data.ContainsKey("recovery_service_active"));
        Assert.True((bool)result.Data["recovery_service_active"]);
        Assert.True(result.Data.ContainsKey("is_recovering"));
        Assert.False((bool)result.Data["is_recovering"]); // Should not be recovering initially
    }

    [Fact]
    public async Task RecoveryHealthCheck_HandlesException_ReturnsUnhealthy()
    {
        // Arrange
        var component = Substitute.For<IRecoverableComponent>();
        component.ComponentName.Returns(_ => throw new InvalidOperationException("Test exception"));

        var healthCheck = new RecoveryHealthCheck(new[] { component });
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
        Assert.Contains("error", result.Description ?? "", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Health Check Response Time Tests

    [Fact]
    public async Task TransportHealthCheck_CompletesQuickly()
    {
        // Arrange
        var transport = Substitute.For<IMessageTransport, IHealthCheckable>();
        transport.Name.Returns("TestTransport");
        
        var healthCheckable = (IHealthCheckable)transport;
        healthCheckable.IsHealthy.Returns(true);
        healthCheckable.HealthStatus.Returns("Connected");

        var healthCheck = new TransportHealthCheck(transport);
        var context = new HealthCheckContext();

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await healthCheck.CheckHealthAsync(context);
        sw.Stop();

        // Assert - Health check should complete in less than 100ms
        Assert.True(sw.ElapsedMilliseconds < 100, 
            $"Health check took {sw.ElapsedMilliseconds}ms, expected < 100ms");
    }

    [Fact]
    public async Task PersistenceHealthCheck_CompletesQuickly_WithMultipleComponents()
    {
        // Arrange
        var components = new List<IHealthCheckable>();
        for (int i = 0; i < 10; i++)
        {
            var component = Substitute.For<IHealthCheckable>();
            component.IsHealthy.Returns(true);
            component.HealthStatus.Returns("OK");
            components.Add(component);
        }

        var healthCheck = new PersistenceHealthCheck(components);
        var context = new HealthCheckContext();

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await healthCheck.CheckHealthAsync(context);
        sw.Stop();

        // Assert - Health check should complete in less than 100ms even with 10 components
        Assert.True(sw.ElapsedMilliseconds < 100, 
            $"Health check took {sw.ElapsedMilliseconds}ms, expected < 100ms");
    }

    [Fact]
    public async Task RecoveryHealthCheck_CompletesQuickly_WithMultipleComponents()
    {
        // Arrange
        var components = new List<IRecoverableComponent>();
        for (int i = 0; i < 10; i++)
        {
            var component = Substitute.For<IRecoverableComponent>();
            component.ComponentName.Returns($"Component{i}");
            component.IsHealthy.Returns(true);
            components.Add(component);
        }

        var healthCheck = new RecoveryHealthCheck(components);
        var context = new HealthCheckContext();

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await healthCheck.CheckHealthAsync(context);
        sw.Stop();

        // Assert - Health check should complete in less than 100ms even with 10 components
        Assert.True(sw.ElapsedMilliseconds < 100, 
            $"Health check took {sw.ElapsedMilliseconds}ms, expected < 100ms");
    }

    #endregion
}
