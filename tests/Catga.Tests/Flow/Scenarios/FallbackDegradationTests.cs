using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Fallback and degradation flow scenario tests.
/// Tests graceful degradation, fallback strategies, and service degradation patterns.
/// </summary>
public class FallbackDegradationTests
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
    public async Task Fallback_PrimaryFails_UsesFallbackValue()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<FallbackState>("primary-fallback")
            .Step("try-primary", async (state, ct) =>
            {
                try
                {
                    if (state.PrimaryAvailable)
                    {
                        state.Result = "Primary result";
                        state.Source = "Primary";
                    }
                    else
                    {
                        throw new InvalidOperationException("Primary unavailable");
                    }
                }
                catch
                {
                    state.Result = "Fallback result";
                    state.Source = "Fallback";
                }
                return true;
            })
            .Build();

        var state = new FallbackState { FlowId = "fallback-test", PrimaryAvailable = false };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Source.Should().Be("Fallback");
        result.State.Result.Should().Be("Fallback result");
    }

    [Fact]
    public async Task Fallback_CascadingFallbacks_TriesAllLevels()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<CascadingFallbackState>("cascading")
            .Step("try-sources", async (state, ct) =>
            {
                // Try primary
                if (state.PrimaryAvailable)
                {
                    state.Result = "Primary";
                    state.AttemptedSources.Add("Primary");
                    return true;
                }
                state.AttemptedSources.Add("Primary-Failed");

                // Try secondary
                if (state.SecondaryAvailable)
                {
                    state.Result = "Secondary";
                    state.AttemptedSources.Add("Secondary");
                    return true;
                }
                state.AttemptedSources.Add("Secondary-Failed");

                // Try tertiary
                if (state.TertiaryAvailable)
                {
                    state.Result = "Tertiary";
                    state.AttemptedSources.Add("Tertiary");
                    return true;
                }
                state.AttemptedSources.Add("Tertiary-Failed");

                // Final fallback - cached/default
                state.Result = "Cached-Default";
                state.AttemptedSources.Add("Cached-Default");
                return true;
            })
            .Build();

        var state = new CascadingFallbackState
        {
            FlowId = "cascade-test",
            PrimaryAvailable = false,
            SecondaryAvailable = false,
            TertiaryAvailable = true
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Result.Should().Be("Tertiary");
        result.State.AttemptedSources.Should().ContainInOrder("Primary-Failed", "Secondary-Failed", "Tertiary");
    }

    [Fact]
    public async Task Degradation_ReducedFunctionality_StillOperational()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<DegradationState>("degraded-mode")
            .Step("check-capabilities", async (state, ct) =>
            {
                state.FullFeaturesAvailable = state.ExternalServicesHealthy;

                if (!state.FullFeaturesAvailable)
                {
                    state.DegradedMode = true;
                    state.DisabledFeatures.Add("RealTimeNotifications");
                    state.DisabledFeatures.Add("AdvancedAnalytics");
                    state.DisabledFeatures.Add("ThirdPartyIntegrations");
                }
                return true;
            })
            .Step("process-request", async (state, ct) =>
            {
                // Core functionality always works
                state.CoreFunctionalityExecuted = true;

                if (!state.DegradedMode)
                {
                    state.AdvancedFeaturesExecuted = true;
                }

                return true;
            })
            .Build();

        var state = new DegradationState { FlowId = "degrade-test", ExternalServicesHealthy = false };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.DegradedMode.Should().BeTrue();
        result.State.CoreFunctionalityExecuted.Should().BeTrue();
        result.State.AdvancedFeaturesExecuted.Should().BeFalse();
    }

    [Fact]
    public async Task Fallback_CachedResponse_ReturnsStaleData()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cache = new SimpleCache<string>();
        cache.Set("product-123", "Cached Product Data", TimeSpan.FromHours(1));

        var flow = FlowBuilder.Create<CacheState>("cache-fallback")
            .Step("fetch-data", async (state, ct) =>
            {
                if (state.LiveDataAvailable)
                {
                    state.Data = "Fresh Live Data";
                    state.DataSource = "Live";
                    cache.Set(state.CacheKey, state.Data, TimeSpan.FromHours(1));
                }
                else
                {
                    var cached = cache.Get(state.CacheKey);
                    if (cached != null)
                    {
                        state.Data = cached;
                        state.DataSource = "Cache";
                        state.IsStaleData = true;
                    }
                    else
                    {
                        state.Data = "Default Data";
                        state.DataSource = "Default";
                    }
                }
                return true;
            })
            .Build();

        var state = new CacheState { FlowId = "cache-test", CacheKey = "product-123", LiveDataAvailable = false };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.DataSource.Should().Be("Cache");
        result.State.IsStaleData.Should().BeTrue();
    }

    [Fact]
    public async Task Fallback_TimeoutTriggersFallback_ReturnsQuickly()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<TimeoutFallbackState>("timeout-fallback")
            .Step("slow-operation", async (state, ct) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMilliseconds(50));

                try
                {
                    if (state.OperationDelayMs > 50)
                    {
                        await Task.Delay(state.OperationDelayMs, cts.Token);
                    }
                    state.Result = "Completed";
                    state.TimedOut = false;
                }
                catch (OperationCanceledException)
                {
                    state.Result = "Timeout Fallback";
                    state.TimedOut = true;
                }
                return true;
            })
            .Build();

        var state = new TimeoutFallbackState { FlowId = "timeout-test", OperationDelayMs = 500 };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.TimedOut.Should().BeTrue();
        result.State.Result.Should().Be("Timeout Fallback");
    }

    [Fact]
    public async Task Degradation_LoadBased_ReducesFeatures()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LoadBasedState>("load-degradation")
            .Step("check-load", async (state, ct) =>
            {
                if (state.CurrentLoad > 90)
                {
                    state.OperationMode = "Emergency";
                    state.EnabledFeatures = new List<string> { "CoreOnly" };
                }
                else if (state.CurrentLoad > 70)
                {
                    state.OperationMode = "Degraded";
                    state.EnabledFeatures = new List<string> { "Core", "BasicAnalytics" };
                }
                else
                {
                    state.OperationMode = "Normal";
                    state.EnabledFeatures = new List<string> { "Core", "Analytics", "Notifications", "Integrations" };
                }
                return true;
            })
            .Build();

        var highLoadState = new LoadBasedState { FlowId = "high-load", CurrentLoad = 95 };
        var result = await executor.ExecuteAsync(flow, highLoadState);

        result.State.OperationMode.Should().Be("Emergency");
        result.State.EnabledFeatures.Should().HaveCount(1);
    }

    [Fact]
    public async Task Fallback_PartialSuccess_ReturnsMixedResults()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<PartialSuccessState>("partial-success")
            .Step("multi-source-fetch", async (state, ct) =>
            {
                // Source A - Success
                if (state.SourceAAvailable)
                {
                    state.Results["SourceA"] = "Real data from A";
                }
                else
                {
                    state.Results["SourceA"] = "Fallback A";
                    state.PartialFailures.Add("SourceA");
                }

                // Source B - Failure with fallback
                if (state.SourceBAvailable)
                {
                    state.Results["SourceB"] = "Real data from B";
                }
                else
                {
                    state.Results["SourceB"] = "Fallback B";
                    state.PartialFailures.Add("SourceB");
                }

                // Source C - Success
                if (state.SourceCAvailable)
                {
                    state.Results["SourceC"] = "Real data from C";
                }
                else
                {
                    state.Results["SourceC"] = "Fallback C";
                    state.PartialFailures.Add("SourceC");
                }

                state.IsPartialSuccess = state.PartialFailures.Count > 0 && state.PartialFailures.Count < 3;
                return true;
            })
            .Build();

        var state = new PartialSuccessState
        {
            FlowId = "partial-test",
            SourceAAvailable = true,
            SourceBAvailable = false,
            SourceCAvailable = true
        };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.IsPartialSuccess.Should().BeTrue();
        result.State.PartialFailures.Should().HaveCount(1);
        result.State.Results["SourceA"].Should().Be("Real data from A");
        result.State.Results["SourceB"].Should().Be("Fallback B");
    }

    [Fact]
    public async Task Fallback_CircuitBreakerTriggered_UsesAlternative()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<CircuitFallbackState>("circuit-fallback")
            .Step("check-circuit", async (state, ct) =>
            {
                if (state.CircuitOpen)
                {
                    state.UsedAlternative = true;
                    state.Result = "Alternative service result";
                    state.ServiceUsed = "Alternative";
                }
                else
                {
                    state.Result = "Primary service result";
                    state.ServiceUsed = "Primary";
                }
                return true;
            })
            .Build();

        var state = new CircuitFallbackState { FlowId = "circuit-fb-test", CircuitOpen = true };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.UsedAlternative.Should().BeTrue();
        result.State.ServiceUsed.Should().Be("Alternative");
    }

    #region State Classes

    public class FallbackState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool PrimaryAvailable { get; set; }
        public string? Result { get; set; }
        public string? Source { get; set; }
    }

    public class CascadingFallbackState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool PrimaryAvailable { get; set; }
        public bool SecondaryAvailable { get; set; }
        public bool TertiaryAvailable { get; set; }
        public string? Result { get; set; }
        public List<string> AttemptedSources { get; set; } = new();
    }

    public class DegradationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool ExternalServicesHealthy { get; set; }
        public bool FullFeaturesAvailable { get; set; }
        public bool DegradedMode { get; set; }
        public List<string> DisabledFeatures { get; set; } = new();
        public bool CoreFunctionalityExecuted { get; set; }
        public bool AdvancedFeaturesExecuted { get; set; }
    }

    public class CacheState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string CacheKey { get; set; } = "";
        public bool LiveDataAvailable { get; set; }
        public string? Data { get; set; }
        public string? DataSource { get; set; }
        public bool IsStaleData { get; set; }
    }

    public class TimeoutFallbackState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int OperationDelayMs { get; set; }
        public string? Result { get; set; }
        public bool TimedOut { get; set; }
    }

    public class LoadBasedState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int CurrentLoad { get; set; }
        public string? OperationMode { get; set; }
        public List<string> EnabledFeatures { get; set; } = new();
    }

    public class PartialSuccessState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool SourceAAvailable { get; set; }
        public bool SourceBAvailable { get; set; }
        public bool SourceCAvailable { get; set; }
        public Dictionary<string, string> Results { get; set; } = new();
        public List<string> PartialFailures { get; set; } = new();
        public bool IsPartialSuccess { get; set; }
    }

    public class CircuitFallbackState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool CircuitOpen { get; set; }
        public bool UsedAlternative { get; set; }
        public string? Result { get; set; }
        public string? ServiceUsed { get; set; }
    }

    public class SimpleCache<T>
    {
        private readonly Dictionary<string, (T Value, DateTime Expiry)> _cache = new();

        public void Set(string key, T value, TimeSpan ttl)
        {
            _cache[key] = (value, DateTime.UtcNow.Add(ttl));
        }

        public T? Get(string key)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.UtcNow)
            {
                return entry.Value;
            }
            return default;
        }
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
