using Catga.HealthCheck;
using Xunit;

namespace Catga.Tests.HealthCheck;

public class HealthCheckServiceTests
{
    [Fact]
    public async Task CheckAllAsync_AllHealthy_ReturnsHealthy()
    {
        // Arrange
        var healthChecks = new IHealthCheck[]
        {
            new TestHealthCheck("Check1", HealthStatus.Healthy),
            new TestHealthCheck("Check2", HealthStatus.Healthy)
        };

        var service = new HealthCheckService(healthChecks);

        // Act
        var report = await service.CheckAllAsync();

        // Assert
        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Equal(2, report.Entries.Count);
        Assert.True(report.IsHealthy);
    }

    [Fact]
    public async Task CheckAllAsync_OneDegraded_ReturnsDegraded()
    {
        // Arrange
        var healthChecks = new IHealthCheck[]
        {
            new TestHealthCheck("Check1", HealthStatus.Healthy),
            new TestHealthCheck("Check2", HealthStatus.Degraded)
        };

        var service = new HealthCheckService(healthChecks);

        // Act
        var report = await service.CheckAllAsync();

        // Assert
        Assert.Equal(HealthStatus.Degraded, report.Status);
        Assert.False(report.IsHealthy);
    }

    [Fact]
    public async Task CheckAllAsync_OneUnhealthy_ReturnsUnhealthy()
    {
        // Arrange
        var healthChecks = new IHealthCheck[]
        {
            new TestHealthCheck("Check1", HealthStatus.Healthy),
            new TestHealthCheck("Check2", HealthStatus.Unhealthy)
        };

        var service = new HealthCheckService(healthChecks);

        // Act
        var report = await service.CheckAllAsync();

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.False(report.IsHealthy);
    }

    [Fact]
    public async Task CheckAllAsync_HealthCheckThrows_ReturnsUnhealthy()
    {
        // Arrange
        var healthChecks = new IHealthCheck[]
        {
            new ThrowingHealthCheck("ThrowingCheck")
        };

        var service = new HealthCheckService(healthChecks);

        // Act
        var report = await service.CheckAllAsync();

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Single(report.Entries);
        Assert.Equal(HealthStatus.Unhealthy, report.Entries["ThrowingCheck"].Status);
        Assert.NotNull(report.Entries["ThrowingCheck"].Exception);
    }

    [Fact]
    public async Task CheckAsync_SpecificCheck_ReturnsResult()
    {
        // Arrange
        var healthChecks = new IHealthCheck[]
        {
            new TestHealthCheck("Check1", HealthStatus.Healthy),
            new TestHealthCheck("Check2", HealthStatus.Degraded)
        };

        var service = new HealthCheckService(healthChecks);

        // Act
        var result = await service.CheckAsync("Check2");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckAsync_NonExistentCheck_ReturnsNull()
    {
        // Arrange
        var healthChecks = new IHealthCheck[]
        {
            new TestHealthCheck("Check1", HealthStatus.Healthy)
        };

        var service = new HealthCheckService(healthChecks);

        // Act
        var result = await service.CheckAsync("NonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task HealthReport_CalculatesTotalDuration()
    {
        // Arrange
        var healthChecks = new IHealthCheck[]
        {
            new SlowHealthCheck("Slow1", 100),
            new SlowHealthCheck("Slow2", 150)
        };

        var service = new HealthCheckService(healthChecks);

        // Act
        var report = await service.CheckAllAsync();

        // Assert
        Assert.True(report.TotalDuration.TotalMilliseconds >= 250);
    }

    private class TestHealthCheck : IHealthCheck
    {
        private readonly HealthStatus _status;

        public string Name { get; }

        public TestHealthCheck(string name, HealthStatus status)
        {
            Name = name;
            _status = status;
        }

        public ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            var result = _status switch
            {
                HealthStatus.Healthy => HealthCheckResult.Healthy($"{Name} is healthy"),
                HealthStatus.Degraded => HealthCheckResult.Degraded($"{Name} is degraded"),
                HealthStatus.Unhealthy => HealthCheckResult.Unhealthy($"{Name} is unhealthy"),
                _ => throw new ArgumentOutOfRangeException()
            };

            return ValueTask.FromResult(result);
        }
    }

    private class ThrowingHealthCheck : IHealthCheck
    {
        public string Name { get; }

        public ThrowingHealthCheck(string name)
        {
            Name = name;
        }

        public ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Health check failed");
        }
    }

    private class SlowHealthCheck : IHealthCheck
    {
        private readonly int _delayMs;

        public string Name { get; }

        public SlowHealthCheck(string name, int delayMs)
        {
            Name = name;
            _delayMs = delayMs;
        }

        public async ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delayMs, cancellationToken);
            return HealthCheckResult.Healthy($"{Name} completed");
        }
    }
}

