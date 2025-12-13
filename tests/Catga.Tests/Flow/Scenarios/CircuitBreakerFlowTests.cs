using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Circuit breaker flow scenario tests.
/// Tests circuit breaker patterns, failure thresholds, and recovery.
/// </summary>
public class CircuitBreakerFlowTests
{
    #region Constants

    private const int DefaultFailureThreshold = 3;
    private const int SmallFailureThreshold = 2;
    private const int DefaultResetTimeoutMs = 30000;
    private const int ShortResetTimeoutMs = 50;
    private const int MediumResetTimeoutMs = 100;
    private const int WaitForHalfOpenMs = 150;

    #endregion

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
    public async Task CircuitBreaker_ClosedState_AllowsRequests()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var breaker = new SimpleCircuitBreaker(DefaultFailureThreshold, TimeSpan.FromMilliseconds(DefaultResetTimeoutMs));
        var flow = FlowBuilder.Create<CircuitBreakerState>("closed-circuit")
            .Step("operation", async (state, ct) =>
            {
                if (!breaker.AllowRequest())
                {
                    state.CircuitOpen = true;
                    return false;
                }
                state.RequestExecuted = true;
                breaker.RecordSuccess();
                return true;
            })
            .Build();
        var state = new CircuitBreakerState { FlowId = "closed-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue("circuit is closed and should allow requests");
        result.State.RequestExecuted.Should().BeTrue("request should execute successfully");
        result.State.CircuitOpen.Should().BeFalse("circuit should remain closed");
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterFailures_BlocksRequests()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var breaker = new SimpleCircuitBreaker(DefaultFailureThreshold, TimeSpan.FromMilliseconds(DefaultResetTimeoutMs));
        var flow = FlowBuilder.Create<CircuitBreakerState>("open-circuit")
            .Step("failing-operation", async (state, ct) =>
            {
                if (!breaker.AllowRequest())
                {
                    state.CircuitOpen = true;
                    state.BlockedByCircuit = true;
                    return false;
                }
                if (state.ShouldFail)
                {
                    breaker.RecordFailure();
                    throw new InvalidOperationException("Simulated failure");
                }
                breaker.RecordSuccess();
                return true;
            })
            .Build();

        // Act - Execute failing requests to open circuit
        for (int i = 0; i < DefaultFailureThreshold; i++)
        {
            var failState = new CircuitBreakerState { FlowId = $"fail-{i}", ShouldFail = true };
            await executor.ExecuteAsync(flow, failState);
        }
        var blockedState = new CircuitBreakerState { FlowId = "blocked" };
        var result = await executor.ExecuteAsync(flow, blockedState);

        // Assert
        result.IsSuccess.Should().BeFalse("circuit should be open after threshold failures");
        result.State.BlockedByCircuit.Should().BeTrue("request should be blocked by open circuit");
    }

    [Fact]
    public async Task CircuitBreaker_HalfOpenState_AllowsProbeRequest()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var breaker = new SimpleCircuitBreaker(SmallFailureThreshold, TimeSpan.FromMilliseconds(MediumResetTimeoutMs));
        var flow = FlowBuilder.Create<CircuitBreakerState>("half-open")
            .Step("operation", async (state, ct) =>
            {
                if (!breaker.AllowRequest())
                {
                    state.BlockedByCircuit = true;
                    return false;
                }
                if (state.ShouldFail)
                {
                    breaker.RecordFailure();
                    return false;
                }
                breaker.RecordSuccess();
                state.RequestExecuted = true;
                return true;
            })
            .Build();

        // Act - Open the circuit
        for (int i = 0; i < SmallFailureThreshold; i++)
        {
            await executor.ExecuteAsync(flow, new CircuitBreakerState { FlowId = $"fail-{i}", ShouldFail = true });
        }
        await Task.Delay(WaitForHalfOpenMs);
        var probeState = new CircuitBreakerState { FlowId = "probe", ShouldFail = false };
        var result = await executor.ExecuteAsync(flow, probeState);

        // Assert
        result.IsSuccess.Should().BeTrue("probe request should succeed in half-open state");
        result.State.RequestExecuted.Should().BeTrue("request should execute during half-open probe");
    }

    [Fact]
    public async Task CircuitBreaker_RecoveryAfterSuccess_ClosesCircuit()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var breaker = new SimpleCircuitBreaker(2, TimeSpan.FromMilliseconds(50));

        var flow = FlowBuilder.Create<CircuitBreakerState>("recovery")
            .Step("operation", async (state, ct) =>
            {
                if (!breaker.AllowRequest())
                {
                    state.BlockedByCircuit = true;
                    return false;
                }

                if (state.ShouldFail)
                {
                    breaker.RecordFailure();
                    return false;
                }

                breaker.RecordSuccess();
                state.RequestExecuted = true;
                return true;
            })
            .Build();

        // Open circuit
        for (int i = 0; i < 2; i++)
        {
            await executor.ExecuteAsync(flow, new CircuitBreakerState { FlowId = $"fail-{i}", ShouldFail = true });
        }

        // Wait and send successful request
        await Task.Delay(100);
        await executor.ExecuteAsync(flow, new CircuitBreakerState { FlowId = "success-1" });

        // Circuit should now allow normal requests
        var normalState = new CircuitBreakerState { FlowId = "normal" };
        var result = await executor.ExecuteAsync(flow, normalState);

        result.IsSuccess.Should().BeTrue();
        result.State.RequestExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task CircuitBreaker_DifferentServices_IndependentCircuits()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var breakerA = new SimpleCircuitBreaker(2, TimeSpan.FromSeconds(30));
        var breakerB = new SimpleCircuitBreaker(2, TimeSpan.FromSeconds(30));

        var flowA = FlowBuilder.Create<MultiServiceState>("service-a")
            .Step("call-service-a", async (state, ct) =>
            {
                if (!breakerA.AllowRequest()) return false;
                if (state.ServiceAFails) { breakerA.RecordFailure(); return false; }
                breakerA.RecordSuccess();
                state.ServiceASuccess = true;
                return true;
            })
            .Build();

        var flowB = FlowBuilder.Create<MultiServiceState>("service-b")
            .Step("call-service-b", async (state, ct) =>
            {
                if (!breakerB.AllowRequest()) return false;
                breakerB.RecordSuccess();
                state.ServiceBSuccess = true;
                return true;
            })
            .Build();

        // Fail service A circuit
        for (int i = 0; i < 2; i++)
        {
            await executor.ExecuteAsync(flowA, new MultiServiceState { FlowId = $"a-fail-{i}", ServiceAFails = true });
        }

        // Service B should still work
        var stateBResult = await executor.ExecuteAsync(flowB, new MultiServiceState { FlowId = "b-1" });

        stateBResult.IsSuccess.Should().BeTrue();
        stateBResult.State.ServiceBSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CircuitBreaker_WithFallback_UsesFallbackWhenOpen()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var breaker = new SimpleCircuitBreaker(1, TimeSpan.FromSeconds(30));

        var flow = FlowBuilder.Create<FallbackState>("circuit-fallback")
            .Step("primary", async (state, ct) =>
            {
                if (!breaker.AllowRequest())
                {
                    state.UsedFallback = true;
                    state.Result = "Fallback result";
                    return true; // Use fallback
                }

                if (state.PrimaryFails)
                {
                    breaker.RecordFailure();
                    state.UsedFallback = true;
                    state.Result = "Fallback after failure";
                    return true;
                }

                breaker.RecordSuccess();
                state.Result = "Primary result";
                return true;
            })
            .Build();

        // Fail to open circuit
        await executor.ExecuteAsync(flow, new FallbackState { FlowId = "fail", PrimaryFails = true });

        // Next request uses fallback
        var fallbackState = new FallbackState { FlowId = "fallback-test" };
        var result = await executor.ExecuteAsync(flow, fallbackState);

        result.IsSuccess.Should().BeTrue();
        result.State.UsedFallback.Should().BeTrue();
        result.State.Result.Should().Be("Fallback result");
    }

    [Fact]
    public async Task CircuitBreaker_FailureRateThreshold_TripsAtPercentage()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var breaker = new RateBasedCircuitBreaker(50, 10, TimeSpan.FromSeconds(30)); // 50% threshold

        var flow = FlowBuilder.Create<CircuitBreakerState>("rate-based")
            .Step("operation", async (state, ct) =>
            {
                if (!breaker.AllowRequest())
                {
                    state.BlockedByCircuit = true;
                    return false;
                }

                if (state.ShouldFail)
                {
                    breaker.RecordFailure();
                    return false;
                }

                breaker.RecordSuccess();
                state.RequestExecuted = true;
                return true;
            })
            .Build();

        // Execute 10 requests: 6 failures, 4 successes (60% failure rate)
        var requests = new List<CircuitBreakerState>();
        for (int i = 0; i < 6; i++) requests.Add(new CircuitBreakerState { FlowId = $"fail-{i}", ShouldFail = true });
        for (int i = 0; i < 4; i++) requests.Add(new CircuitBreakerState { FlowId = $"success-{i}", ShouldFail = false });

        foreach (var req in requests)
        {
            await executor.ExecuteAsync(flow, req);
        }

        // Circuit should be open (>50% failure)
        var checkState = new CircuitBreakerState { FlowId = "check" };
        var result = await executor.ExecuteAsync(flow, checkState);

        result.State.BlockedByCircuit.Should().BeTrue();
    }

    [Fact]
    public async Task CircuitBreaker_SlowCallThreshold_OpensOnSlowResponses()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var breaker = new SlowCallCircuitBreaker(TimeSpan.FromMilliseconds(50), 3, TimeSpan.FromSeconds(30));

        var flow = FlowBuilder.Create<SlowCallState>("slow-call")
            .Step("operation", async (state, ct) =>
            {
                if (!breaker.AllowRequest())
                {
                    state.BlockedByCircuit = true;
                    return false;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                await Task.Delay(state.DelayMs, ct);
                sw.Stop();

                if (sw.Elapsed > TimeSpan.FromMilliseconds(50))
                {
                    breaker.RecordSlowCall();
                }
                else
                {
                    breaker.RecordSuccess();
                }

                state.RequestExecuted = true;
                return true;
            })
            .Build();

        // Execute 3 slow calls
        for (int i = 0; i < 3; i++)
        {
            await executor.ExecuteAsync(flow, new SlowCallState { FlowId = $"slow-{i}", DelayMs = 100 });
        }

        // Circuit should be open
        var checkState = new SlowCallState { FlowId = "check", DelayMs = 10 };
        var result = await executor.ExecuteAsync(flow, checkState);

        result.State.BlockedByCircuit.Should().BeTrue();
    }

    #region State Classes

    public class CircuitBreakerState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool ShouldFail { get; set; }
        public bool RequestExecuted { get; set; }
        public bool CircuitOpen { get; set; }
        public bool BlockedByCircuit { get; set; }
    }

    public class MultiServiceState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool ServiceAFails { get; set; }
        public bool ServiceASuccess { get; set; }
        public bool ServiceBSuccess { get; set; }
    }

    public class FallbackState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool PrimaryFails { get; set; }
        public bool UsedFallback { get; set; }
        public string? Result { get; set; }
    }

    public class SlowCallState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int DelayMs { get; set; }
        public bool RequestExecuted { get; set; }
        public bool BlockedByCircuit { get; set; }
    }

    #endregion

    #region Circuit Breaker Implementations

    public class SimpleCircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly TimeSpan _openDuration;
        private int _failureCount;
        private DateTime _openedAt;
        private CircuitState _state = CircuitState.Closed;
        private readonly object _lock = new();

        public SimpleCircuitBreaker(int failureThreshold, TimeSpan openDuration)
        {
            _failureThreshold = failureThreshold;
            _openDuration = openDuration;
        }

        public bool AllowRequest()
        {
            lock (_lock)
            {
                if (_state == CircuitState.Closed) return true;
                if (_state == CircuitState.Open)
                {
                    if (DateTime.UtcNow - _openedAt >= _openDuration)
                    {
                        _state = CircuitState.HalfOpen;
                        return true;
                    }
                    return false;
                }
                return true; // HalfOpen allows one request
            }
        }

        public void RecordSuccess()
        {
            lock (_lock)
            {
                _failureCount = 0;
                _state = CircuitState.Closed;
            }
        }

        public void RecordFailure()
        {
            lock (_lock)
            {
                _failureCount++;
                if (_failureCount >= _failureThreshold)
                {
                    _state = CircuitState.Open;
                    _openedAt = DateTime.UtcNow;
                }
            }
        }
    }

    public class RateBasedCircuitBreaker
    {
        private readonly int _failureRateThreshold;
        private readonly int _minimumCalls;
        private readonly TimeSpan _openDuration;
        private readonly Queue<bool> _results = new();
        private DateTime _openedAt;
        private bool _isOpen;
        private readonly object _lock = new();

        public RateBasedCircuitBreaker(int failureRateThreshold, int minimumCalls, TimeSpan openDuration)
        {
            _failureRateThreshold = failureRateThreshold;
            _minimumCalls = minimumCalls;
            _openDuration = openDuration;
        }

        public bool AllowRequest()
        {
            lock (_lock)
            {
                if (!_isOpen) return true;
                if (DateTime.UtcNow - _openedAt >= _openDuration)
                {
                    _isOpen = false;
                    _results.Clear();
                    return true;
                }
                return false;
            }
        }

        public void RecordSuccess() => Record(true);
        public void RecordFailure() => Record(false);

        private void Record(bool success)
        {
            lock (_lock)
            {
                _results.Enqueue(success);
                while (_results.Count > _minimumCalls) _results.Dequeue();

                if (_results.Count >= _minimumCalls)
                {
                    var failureRate = (double)_results.Count(r => !r) / _results.Count * 100;
                    if (failureRate >= _failureRateThreshold)
                    {
                        _isOpen = true;
                        _openedAt = DateTime.UtcNow;
                    }
                }
            }
        }
    }

    public class SlowCallCircuitBreaker
    {
        private readonly TimeSpan _slowCallThreshold;
        private readonly int _slowCallLimit;
        private readonly TimeSpan _openDuration;
        private int _slowCallCount;
        private DateTime _openedAt;
        private bool _isOpen;
        private readonly object _lock = new();

        public SlowCallCircuitBreaker(TimeSpan slowCallThreshold, int slowCallLimit, TimeSpan openDuration)
        {
            _slowCallThreshold = slowCallThreshold;
            _slowCallLimit = slowCallLimit;
            _openDuration = openDuration;
        }

        public bool AllowRequest()
        {
            lock (_lock)
            {
                if (!_isOpen) return true;
                if (DateTime.UtcNow - _openedAt >= _openDuration)
                {
                    _isOpen = false;
                    _slowCallCount = 0;
                    return true;
                }
                return false;
            }
        }

        public void RecordSuccess()
        {
            lock (_lock) { _slowCallCount = Math.Max(0, _slowCallCount - 1); }
        }

        public void RecordSlowCall()
        {
            lock (_lock)
            {
                _slowCallCount++;
                if (_slowCallCount >= _slowCallLimit)
                {
                    _isOpen = true;
                    _openedAt = DateTime.UtcNow;
                }
            }
        }
    }

    public enum CircuitState { Closed, Open, HalfOpen }

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
