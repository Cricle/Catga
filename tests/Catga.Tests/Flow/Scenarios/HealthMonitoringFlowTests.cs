using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Health monitoring flow scenario tests.
/// Tests health checks, liveness probes, readiness probes, and service monitoring.
/// </summary>
public class HealthMonitoringFlowTests
{
    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Health_AllServicesHealthy_ReturnsHealthy()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var services = new Dictionary<string, bool>
        {
            ["Database"] = true,
            ["Cache"] = true,
            ["MessageQueue"] = true
        };

        var flow = FlowBuilder.Create<HealthState>("health-check")
            .ForEach(
                s => s.ServicesToCheck,
                (service, f) => f.Step($"check-{service}", async (state, ct) =>
                {
                    var isHealthy = services.GetValueOrDefault(service, false);
                    state.Results[service] = isHealthy ? "Healthy" : "Unhealthy";
                    return true;
                }))
            .Step("aggregate", async (state, ct) =>
            {
                state.OverallStatus = state.Results.Values.All(v => v == "Healthy")
                    ? "Healthy"
                    : "Unhealthy";
                return true;
            })
            .Build();

        var state = new HealthState
        {
            FlowId = "health-test",
            ServicesToCheck = new List<string> { "Database", "Cache", "MessageQueue" }
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.State.OverallStatus.Should().Be("Healthy");
    }

    [Fact]
    public async Task Health_OneServiceUnhealthy_ReturnsUnhealthy()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var services = new Dictionary<string, bool>
        {
            ["Database"] = true,
            ["Cache"] = false, // Unhealthy
            ["MessageQueue"] = true
        };

        var flow = FlowBuilder.Create<HealthState>("partial-healthy")
            .ForEach(
                s => s.ServicesToCheck,
                (service, f) => f.Step($"check-{service}", async (state, ct) =>
                {
                    var isHealthy = services.GetValueOrDefault(service, false);
                    state.Results[service] = isHealthy ? "Healthy" : "Unhealthy";
                    return true;
                }))
            .Step("aggregate", async (state, ct) =>
            {
                state.OverallStatus = state.Results.Values.All(v => v == "Healthy")
                    ? "Healthy"
                    : "Unhealthy";
                return true;
            })
            .Build();

        var state = new HealthState
        {
            FlowId = "partial-test",
            ServicesToCheck = new List<string> { "Database", "Cache", "MessageQueue" }
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.State.OverallStatus.Should().Be("Unhealthy");
        result.State.Results["Cache"].Should().Be("Unhealthy");
    }

    [Fact]
    public async Task Health_LivenessProbe_ChecksBasicFunctionality()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LivenessState>("liveness")
            .Step("check-process", async (state, ct) =>
            {
                state.ProcessRunning = true; // Process is running if we reach here
                return true;
            })
            .Step("check-memory", async (state, ct) =>
            {
                var memory = GC.GetTotalMemory(false);
                state.MemoryUsageMB = memory / (1024 * 1024);
                state.MemoryOk = state.MemoryUsageMB < 1000; // Less than 1GB
                return true;
            })
            .Step("check-threads", async (state, ct) =>
            {
                state.ThreadCount = ThreadPool.ThreadCount;
                state.ThreadsOk = state.ThreadCount < 1000;
                return true;
            })
            .Step("result", async (state, ct) =>
            {
                state.IsAlive = state.ProcessRunning && state.MemoryOk && state.ThreadsOk;
                return true;
            })
            .Build();

        var state = new LivenessState { FlowId = "liveness-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.State.IsAlive.Should().BeTrue();
    }

    [Fact]
    public async Task Health_ReadinessProbe_ChecksDependencies()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var dbConnected = true;
        var cacheConnected = true;

        var flow = FlowBuilder.Create<ReadinessState>("readiness")
            .Step("check-database", async (state, ct) =>
            {
                state.DatabaseReady = dbConnected;
                return true;
            })
            .Step("check-cache", async (state, ct) =>
            {
                state.CacheReady = cacheConnected;
                return true;
            })
            .Step("check-config", async (state, ct) =>
            {
                state.ConfigLoaded = true; // Config is always loaded in tests
                return true;
            })
            .Step("result", async (state, ct) =>
            {
                state.IsReady = state.DatabaseReady && state.CacheReady && state.ConfigLoaded;
                return true;
            })
            .Build();

        var state = new ReadinessState { FlowId = "readiness-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.State.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task Health_MetricsCollection_GathersStats()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var metrics = new MetricsCollector();

        var flow = FlowBuilder.Create<MetricsState>("metrics")
            .Step("collect-request-metrics", async (state, ct) =>
            {
                state.RequestsPerSecond = metrics.GetRequestRate();
                state.AverageLatencyMs = metrics.GetAverageLatency();
                state.ErrorRate = metrics.GetErrorRate();
                return true;
            })
            .Step("collect-resource-metrics", async (state, ct) =>
            {
                state.CpuUsagePercent = 25.5; // Simulated
                state.MemoryUsageMB = GC.GetTotalMemory(false) / (1024.0 * 1024);
                state.ActiveConnections = 50; // Simulated
                return true;
            })
            .Step("check-thresholds", async (state, ct) =>
            {
                state.Alerts = new List<string>();

                if (state.ErrorRate > 0.05)
                    state.Alerts.Add("High error rate");
                if (state.CpuUsagePercent > 80)
                    state.Alerts.Add("High CPU usage");
                if (state.MemoryUsageMB > 500)
                    state.Alerts.Add("High memory usage");

                return true;
            })
            .Build();

        var state = new MetricsState { FlowId = "metrics-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.State.RequestsPerSecond.Should().BeGreaterOrEqualTo(0);
        result.State.Alerts.Should().NotBeNull();
    }

    [Fact]
    public async Task Health_PeriodicHealthCheck_DetectsChanges()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var healthHistory = new ConcurrentQueue<(DateTime Time, string Status)>();
        var serviceHealth = true;

        var flow = FlowBuilder.Create<PeriodicHealthState>("periodic-health")
            .Step("check", async (state, ct) =>
            {
                state.CurrentStatus = serviceHealth ? "Healthy" : "Unhealthy";
                state.CheckTime = DateTime.UtcNow;
                healthHistory.Enqueue((state.CheckTime, state.CurrentStatus));
                return true;
            })
            .Build();

        // First check - healthy
        await executor.ExecuteAsync(flow, new PeriodicHealthState { FlowId = "check-1" });

        // Change status
        serviceHealth = false;

        // Second check - unhealthy
        await executor.ExecuteAsync(flow, new PeriodicHealthState { FlowId = "check-2" });

        // Change back
        serviceHealth = true;

        // Third check - healthy again
        await executor.ExecuteAsync(flow, new PeriodicHealthState { FlowId = "check-3" });

        healthHistory.Should().HaveCount(3);
        var history = healthHistory.ToArray();
        history[0].Status.Should().Be("Healthy");
        history[1].Status.Should().Be("Unhealthy");
        history[2].Status.Should().Be("Healthy");
    }

    [Fact]
    public async Task Health_DeepHealthCheck_ValidatesDataIntegrity()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<DeepHealthState>("deep-health")
            .Step("check-database-connection", async (state, ct) =>
            {
                state.DatabaseConnectionOk = true;
                return true;
            })
            .Step("check-database-query", async (state, ct) =>
            {
                // Simulate query execution
                state.DatabaseQueryOk = true;
                state.DatabaseQueryLatencyMs = 15;
                return true;
            })
            .Step("check-cache-connection", async (state, ct) =>
            {
                state.CacheConnectionOk = true;
                return true;
            })
            .Step("check-cache-read-write", async (state, ct) =>
            {
                // Simulate read/write test
                var testKey = $"health-check-{Guid.NewGuid():N}";
                state.CacheReadWriteOk = true;
                return true;
            })
            .Step("check-external-api", async (state, ct) =>
            {
                // Simulate API call
                state.ExternalApiOk = true;
                state.ExternalApiLatencyMs = 50;
                return true;
            })
            .Step("aggregate", async (state, ct) =>
            {
                state.AllDeepChecksOk = state.DatabaseConnectionOk
                    && state.DatabaseQueryOk
                    && state.CacheConnectionOk
                    && state.CacheReadWriteOk
                    && state.ExternalApiOk;
                return true;
            })
            .Build();

        var state = new DeepHealthState { FlowId = "deep-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.State.AllDeepChecksOk.Should().BeTrue();
    }

    [Fact]
    public async Task Health_GracefulDegradation_PartialFunctionality()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<DegradedHealthState>("degraded-health")
            .Step("check-services", async (state, ct) =>
            {
                state.CoreServicesOk = true;
                state.EnhancedServicesOk = false; // Degraded
                state.OptionalServicesOk = false;
                return true;
            })
            .Step("determine-mode", async (state, ct) =>
            {
                if (state.CoreServicesOk && state.EnhancedServicesOk && state.OptionalServicesOk)
                    state.OperationMode = "Full";
                else if (state.CoreServicesOk && state.EnhancedServicesOk)
                    state.OperationMode = "Standard";
                else if (state.CoreServicesOk)
                    state.OperationMode = "Degraded";
                else
                    state.OperationMode = "Offline";

                return true;
            })
            .Build();

        var state = new DegradedHealthState { FlowId = "degraded-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.State.OperationMode.Should().Be("Degraded");
    }

    #region State Classes

    public class HealthState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> ServicesToCheck { get; set; } = new();
        public Dictionary<string, string> Results { get; set; } = new();
        public string? OverallStatus { get; set; }
    }

    public class LivenessState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool ProcessRunning { get; set; }
        public long MemoryUsageMB { get; set; }
        public bool MemoryOk { get; set; }
        public int ThreadCount { get; set; }
        public bool ThreadsOk { get; set; }
        public bool IsAlive { get; set; }
    }

    public class ReadinessState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool DatabaseReady { get; set; }
        public bool CacheReady { get; set; }
        public bool ConfigLoaded { get; set; }
        public bool IsReady { get; set; }
    }

    public class MetricsState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public double RequestsPerSecond { get; set; }
        public double AverageLatencyMs { get; set; }
        public double ErrorRate { get; set; }
        public double CpuUsagePercent { get; set; }
        public double MemoryUsageMB { get; set; }
        public int ActiveConnections { get; set; }
        public List<string> Alerts { get; set; } = new();
    }

    public class PeriodicHealthState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string? CurrentStatus { get; set; }
        public DateTime CheckTime { get; set; }
    }

    public class DeepHealthState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool DatabaseConnectionOk { get; set; }
        public bool DatabaseQueryOk { get; set; }
        public double DatabaseQueryLatencyMs { get; set; }
        public bool CacheConnectionOk { get; set; }
        public bool CacheReadWriteOk { get; set; }
        public bool ExternalApiOk { get; set; }
        public double ExternalApiLatencyMs { get; set; }
        public bool AllDeepChecksOk { get; set; }
    }

    public class DegradedHealthState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool CoreServicesOk { get; set; }
        public bool EnhancedServicesOk { get; set; }
        public bool OptionalServicesOk { get; set; }
        public string? OperationMode { get; set; }
    }

    public class MetricsCollector
    {
        public double GetRequestRate() => 150.5;
        public double GetAverageLatency() => 45.2;
        public double GetErrorRate() => 0.02;
    }

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
