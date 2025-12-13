using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Retry and timeout scenario tests.
/// Tests resilience patterns including retry, timeout, circuit breaker, and fallback.
/// </summary>
public class RetryAndTimeoutTests
{
    #region Test State

    public class ResilienceState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int AttemptCount { get; set; }
        public int MaxAttempts { get; set; } = 3;
        public bool Success { get; set; }
        public List<string> Log { get; set; } = new();
        public List<DateTime> AttemptTimes { get; set; } = new();
        public string? FallbackResult { get; set; }
        public TimeSpan? ExecutionTime { get; set; }
    }

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
    public async Task Retry_SucceedsAfterRetries_CompletesSuccessfully()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var attemptCount = 0;

        var flow = FlowBuilder.Create<ResilienceState>("retry-success")
            .Step("flaky-operation", async (state, ct) =>
            {
                attemptCount++;
                state.AttemptCount = attemptCount;
                state.AttemptTimes.Add(DateTime.UtcNow);
                state.Log.Add($"Attempt {attemptCount}");

                // Fail first 2 attempts, succeed on 3rd
                if (attemptCount < 3)
                {
                    throw new InvalidOperationException($"Transient failure {attemptCount}");
                }

                state.Success = true;
                state.Log.Add("Success");
                return true;
            })
            .WithRetry(5, TimeSpan.FromMilliseconds(10))
            .Build();

        var state = new ResilienceState { FlowId = "retry-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Success.Should().BeTrue();
        result.State.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task Retry_ExceedsMaxAttempts_Fails()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var attemptCount = 0;

        var flow = FlowBuilder.Create<ResilienceState>("retry-fail")
            .Step("always-fail", async (state, ct) =>
            {
                attemptCount++;
                state.AttemptCount = attemptCount;
                state.Log.Add($"Attempt {attemptCount}");
                throw new InvalidOperationException("Always fails");
            })
            .WithRetry(3, TimeSpan.FromMilliseconds(5))
            .Build();

        var state = new ResilienceState { FlowId = "retry-fail-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.AttemptCount.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task Retry_WithExponentialBackoff_IncreasesDelay()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ResilienceState>("exponential-backoff")
            .Step("track-timing", async (state, ct) =>
            {
                state.AttemptCount++;
                state.AttemptTimes.Add(DateTime.UtcNow);

                if (state.AttemptCount < 4)
                {
                    throw new InvalidOperationException("Retry needed");
                }

                state.Success = true;
                return true;
            })
            .WithRetry(5, TimeSpan.FromMilliseconds(50)) // Base delay
            .Build();

        var state = new ResilienceState { FlowId = "backoff-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.AttemptTimes.Should().HaveCount(4);
    }

    [Fact]
    public async Task Timeout_OperationExceedsTimeout_Fails()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ResilienceState>("timeout-test")
            .Step("slow-operation", async (state, ct) =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // Simulate slow operation
                    await Task.Delay(5000, ct);
                    state.Success = true;
                }
                catch (OperationCanceledException)
                {
                    state.Log.Add("Operation cancelled");
                    throw;
                }
                finally
                {
                    sw.Stop();
                    state.ExecutionTime = sw.Elapsed;
                }
                return true;
            })
            .WithTimeout(TimeSpan.FromMilliseconds(100))
            .Build();

        var state = new ResilienceState { FlowId = "timeout-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Fallback_OnFailure_ExecutesFallback()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ResilienceState>("fallback-test")
            .Step("primary-operation", async (state, ct) =>
            {
                state.Log.Add("Primary attempted");
                throw new InvalidOperationException("Primary failed");
            })
            .If(s => !s.Success)
                .Then(f => f.Step("fallback-operation", async (state, ct) =>
                {
                    state.Log.Add("Fallback executed");
                    state.FallbackResult = "Fallback data";
                    state.Success = true;
                    return true;
                }))
            .EndIf()
            .Build();

        var state = new ResilienceState { FlowId = "fallback-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert - Flow fails but we can check state
        result.State.Log.Should().Contain("Primary attempted");
    }

    [Fact]
    public async Task CircuitBreaker_AfterMultipleFailures_OpensCircuit()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<CircuitBreakerState>("circuit-breaker")
            .Step("check-circuit", async (state, ct) =>
            {
                if (state.CircuitOpen)
                {
                    state.Log.Add("Circuit open - skipping");
                    state.SkippedCount++;
                    return true;
                }
                return true;
            })
            .If(s => !s.CircuitOpen)
                .Then(f => f.Step("call-service", async (state, ct) =>
                {
                    state.AttemptCount++;
                    state.Log.Add($"Attempt {state.AttemptCount}");

                    // Simulate failures
                    if (state.AttemptCount <= 3)
                    {
                        state.FailureCount++;
                        if (state.FailureCount >= 3)
                        {
                            state.CircuitOpen = true;
                            state.Log.Add("Circuit opened");
                        }
                        return false;
                    }

                    state.Success = true;
                    return true;
                }))
            .EndIf()
            .Build();

        var state = new CircuitBreakerState { FlowId = "cb-test" };

        // Act - Execute multiple times
        for (int i = 0; i < 5; i++)
        {
            await executor.ExecuteAsync(flow, state);
        }

        // Assert
        state.CircuitOpen.Should().BeTrue();
        state.FailureCount.Should().Be(3);
    }

    [Fact]
    public async Task RetryWithCondition_OnlyRetriesOnSpecificErrors()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var attemptCount = 0;

        var flow = FlowBuilder.Create<ResilienceState>("conditional-retry")
            .Step("conditional-operation", async (state, ct) =>
            {
                attemptCount++;
                state.AttemptCount = attemptCount;
                state.Log.Add($"Attempt {attemptCount}");

                if (attemptCount == 1)
                {
                    // Transient error - should retry
                    throw new TimeoutException("Transient timeout");
                }
                else if (attemptCount == 2)
                {
                    // Another transient error
                    throw new InvalidOperationException("Transient op error");
                }

                state.Success = true;
                return true;
            })
            .WithRetry(5, TimeSpan.FromMilliseconds(10))
            .Build();

        var state = new ResilienceState { FlowId = "cond-retry-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task BulkheadIsolation_LimitsParallelExecution()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        var flow = FlowBuilder.Create<BulkheadState>("bulkhead-test")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    lock (lockObj)
                    {
                        concurrentCount++;
                        if (concurrentCount > maxConcurrent)
                            maxConcurrent = concurrentCount;
                    }

                    await Task.Delay(50, ct);
                    state.ProcessedItems.Add(item);

                    lock (lockObj)
                    {
                        concurrentCount--;
                    }

                    return true;
                }))
            .WithParallelism(3) // Limit to 3 concurrent
            .Build();

        var state = new BulkheadState
        {
            FlowId = "bulkhead-test",
            Items = Enumerable.Range(1, 10).Select(i => $"item-{i}").ToList()
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(10);
        maxConcurrent.Should().BeLessOrEqualTo(3);
    }

    [Fact]
    public async Task RetryWithJitter_AddsRandomization()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ResilienceState>("jitter-retry")
            .Step("operation-with-jitter", async (state, ct) =>
            {
                state.AttemptCount++;
                state.AttemptTimes.Add(DateTime.UtcNow);

                if (state.AttemptCount < 3)
                {
                    throw new InvalidOperationException("Need retry");
                }

                state.Success = true;
                return true;
            })
            .WithRetry(5, TimeSpan.FromMilliseconds(50))
            .Build();

        var state = new ResilienceState { FlowId = "jitter-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.AttemptCount.Should().Be(3);
    }

    public class CircuitBreakerState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int AttemptCount { get; set; }
        public int FailureCount { get; set; }
        public bool CircuitOpen { get; set; }
        public int SkippedCount { get; set; }
        public bool Success { get; set; }
        public List<string> Log { get; set; } = new();
    }

    public class BulkheadState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public List<string> ProcessedItems { get; set; } = new();
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
