using Catga.Tests.Framework.Generators;
using Xunit;

namespace Catga.Tests.Framework;

/// <summary>
/// 验证测试框架基础设施是否正常工作
/// </summary>
public class FrameworkVerificationTests
{
    [Fact]
    public void FaultInjectionMiddleware_InjectFault_ConfiguresCorrectly()
    {
        // Arrange
        var middleware = new FaultInjectionMiddleware();

        // Act
        middleware.InjectFault(FaultInjectionMiddleware.FaultType.NetworkTimeout, 0.5);
        var stats = middleware.GetStatistics();

        // Assert
        Assert.Equal(1, stats.TotalFaults);
        Assert.Equal(1, stats.EnabledFaults);
        Assert.Contains(FaultInjectionMiddleware.FaultType.NetworkTimeout, stats.FaultTypes);
    }

    [Fact]
    public void FaultInjectionMiddleware_ClearAllFaults_RemovesAllFaults()
    {
        // Arrange
        var middleware = new FaultInjectionMiddleware();
        middleware.InjectFault(FaultInjectionMiddleware.FaultType.NetworkTimeout);
        middleware.InjectFault(FaultInjectionMiddleware.FaultType.ConnectionFailure);

        // Act
        middleware.ClearAllFaults();
        var stats = middleware.GetStatistics();

        // Assert
        Assert.Equal(0, stats.TotalFaults);
    }

    [Fact]
    public async Task PerformanceBenchmarkFramework_MeasureAsync_ReturnsValidMetrics()
    {
        // Arrange
        var framework = new PerformanceBenchmarkFramework();
        var operationCount = 0;

        // Act
        var measurement = await framework.MeasureAsync(
            async () =>
            {
                await Task.Delay(1);
                operationCount++;
            },
            iterations: 100,
            warmupIterations: 10);

        // Assert
        Assert.Equal(100, measurement.TotalOperations);
        Assert.True(measurement.ThroughputOpsPerSec > 0);
        Assert.Equal(100, measurement.LatenciesMs.Count);
        Assert.True(measurement.LatencyP50Ms > 0);
        Assert.True(measurement.LatencyP95Ms >= measurement.LatencyP50Ms);
        Assert.True(measurement.LatencyP99Ms >= measurement.LatencyP95Ms);
    }

    [Fact]
    public async Task PerformanceBenchmarkFramework_SaveAndLoadBaseline_WorksCorrectly()
    {
        // Arrange - 使用唯一的临时目录避免并行测试冲突
        var uniqueDir = Path.Combine(Path.GetTempPath(), $"catga-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(uniqueDir);
        
        try
        {
            var framework = new PerformanceBenchmarkFramework(uniqueDir);
            var baseline = new PerformanceBenchmarkFramework.Baseline
            {
                ThroughputOpsPerSec = 10000,
                LatencyP99Ms = 5.0,
                LatencyP95Ms = 3.0,
                LatencyP50Ms = 1.0,
                MemoryUsageBytes = 1024 * 1024,
                StartupTime = TimeSpan.FromSeconds(2),
                MeasuredAt = DateTime.UtcNow,
                TestName = "Test",
                BackendType = "InMemory"
            };

            // Act
            await framework.SaveBaselineAsync(baseline);
            var loaded = await framework.LoadBaselineAsync();

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(baseline.ThroughputOpsPerSec, loaded.ThroughputOpsPerSec);
            Assert.Equal(baseline.LatencyP99Ms, loaded.LatencyP99Ms);
            Assert.Equal(baseline.TestName, loaded.TestName);
        }
        finally
        {
            // 清理临时目录
            try
            {
                if (Directory.Exists(uniqueDir))
                {
                    Directory.Delete(uniqueDir, recursive: true);
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }
    }

    [Fact]
    public void BackendMatrixTestFramework_GetAllCombinations_Returns27Combinations()
    {
        // Act
        var combinations = BackendMatrixTestFramework.GetAllCombinations().ToList();

        // Assert
        Assert.Equal(27, combinations.Count);
        
        // Verify all combinations are unique
        var uniqueCombinations = combinations.Distinct().ToList();
        Assert.Equal(27, uniqueCombinations.Count);
    }

    [Fact]
    public void BackendMatrixTestFramework_GetCombinationName_ReturnsCorrectFormat()
    {
        // Arrange
        var eventStore = PropertyTests.BackendType.InMemory;
        var transport = PropertyTests.BackendType.Redis;
        var flowStore = PropertyTests.BackendType.Nats;

        // Act
        var name = BackendMatrixTestFramework.GetCombinationName(eventStore, transport, flowStore);

        // Assert
        Assert.Equal("InMemory+Redis+Nats", name);
    }

    [Fact]
    public void TenantGenerators_GenerateTenant_CreatesValidTenant()
    {
        // Act
        var tenant = TenantGenerators.GenerateTenant();

        // Assert
        Assert.NotNull(tenant);
        Assert.NotEmpty(tenant.TenantId);
        Assert.NotEmpty(tenant.TenantName);
        Assert.NotNull(tenant.Configuration);
        Assert.NotNull(tenant.Limits);
        Assert.True(tenant.Limits.MaxOrders > 0);
        Assert.True(tenant.Limits.MaxEventsPerSecond > 0);
        Assert.True(tenant.Limits.MaxStorageBytes > 0);
    }

    [Fact]
    public void TenantGenerators_GenerateTenantPair_CreatesTwoDifferentTenants()
    {
        // Act
        var (tenantA, tenantB) = TenantGenerators.GenerateTenantPair();

        // Assert
        Assert.NotEqual(tenantA.TenantId, tenantB.TenantId);
        Assert.NotEqual(tenantA.TenantName, tenantB.TenantName);
    }

    [Fact]
    public void SagaGenerators_GenerateSaga_CreatesValidSaga()
    {
        // Act
        var saga = SagaGenerators.GenerateSaga(3, 5);

        // Assert
        Assert.NotNull(saga);
        Assert.NotEmpty(saga.SagaId);
        Assert.NotEmpty(saga.SagaName);
        Assert.InRange(saga.Steps.Count, 3, 5);
        
        foreach (var step in saga.Steps)
        {
            Assert.NotEmpty(step.StepId);
            Assert.NotEmpty(step.StepName);
            Assert.NotNull(step.Execute);
            Assert.NotNull(step.Compensate);
            Assert.True(step.Timeout > TimeSpan.Zero);
        }
    }

    [Fact]
    public void SagaGenerators_GenerateSagaWithFailureAt_CreatesCorrectFailurePoint()
    {
        // Act
        var saga = SagaGenerators.GenerateSagaWithFailureAt(5, 3);

        // Assert
        Assert.Equal(5, saga.Steps.Count);
        Assert.Contains("FailAt3", saga.SagaName);
    }

    [Fact]
    public void PerformanceGenerators_GenerateMetrics_CreatesValidMetrics()
    {
        // Act
        var metrics = PerformanceGenerators.GenerateMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.True(metrics.ThroughputOpsPerSec > 0);
        Assert.NotNull(metrics.Latency);
        Assert.True(metrics.Latency.P50Ms >= 0);
        Assert.True(metrics.Latency.P95Ms >= metrics.Latency.P50Ms);
        Assert.True(metrics.Latency.P99Ms >= metrics.Latency.P95Ms);
        Assert.True(metrics.Latency.P999Ms >= metrics.Latency.P99Ms);
        Assert.NotNull(metrics.Resources);
        Assert.True(metrics.Resources.MemoryBytes > 0);
    }

    [Fact]
    public void PerformanceGenerators_GenerateTimeTravelQuery_CreatesValidQuery()
    {
        // Act
        var query = PerformanceGenerators.GenerateTimeTravelQuery();

        // Assert
        Assert.NotNull(query);
        Assert.NotEmpty(query.AggregateId);
        Assert.True(query.Timestamp < DateTime.UtcNow);
    }

    [Fact]
    public void PerformanceGenerators_GenerateLargeEventData_CreatesCorrectSize()
    {
        // Act
        var events = PerformanceGenerators.GenerateLargeEventData(10, 5);

        // Assert
        Assert.Equal(10, events.Count);
        foreach (var eventData in events)
        {
            Assert.Equal(5 * 1024, eventData.Length);
        }
    }
}
