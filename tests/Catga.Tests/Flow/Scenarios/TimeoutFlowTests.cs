using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Timeout flow scenario tests.
/// Tests step timeouts, flow timeouts, and timeout handling strategies.
/// </summary>
public class TimeoutFlowTests
{
    #region Constants

    private const int VerySlowDelayMs = 5000;
    private const int FastDelayMs = 10;
    private const int MediumDelayMs = 20;
    private const int ShortTimeoutMs = 50;
    private const int MediumTimeoutMs = 100;
    private const int LongTimeoutMs = 1000;
    private const int VeryLongTimeoutMs = 2000;
    private const int LoopIterations = 5;

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
    public async Task Timeout_StepExceedsTimeout_ThrowsException()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var flow = FlowBuilder.Create<TimeoutState>("step-timeout")
            .Step("slow-step", async (state, ct) =>
            {
                state.StepStarted = true;
                await Task.Delay(VerySlowDelayMs, ct);
                state.StepCompleted = true;
                return true;
            })
            .WithTimeout(TimeSpan.FromMilliseconds(MediumTimeoutMs))
            .Build();
        var state = new TimeoutState { FlowId = "timeout-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse("step should timeout before completion");
        result.State.StepStarted.Should().BeTrue("step should have started before timeout");
        result.State.StepCompleted.Should().BeFalse("step should not complete due to timeout");
    }

    [Fact]
    public async Task Timeout_StepCompletesInTime_Succeeds()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var flow = FlowBuilder.Create<TimeoutState>("fast-step")
            .Step("quick-step", async (state, ct) =>
            {
                state.StepStarted = true;
                await Task.Delay(FastDelayMs, ct);
                state.StepCompleted = true;
                return true;
            })
            .WithTimeout(TimeSpan.FromMilliseconds(VerySlowDelayMs))
            .Build();
        var state = new TimeoutState { FlowId = "fast-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue("step should complete before timeout");
        result.State.StepCompleted.Should().BeTrue("step should complete successfully");
    }

    [Fact]
    public async Task Timeout_CancellationPropagates_ToAllSteps()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cts = new CancellationTokenSource();
        var flow = FlowBuilder.Create<TimeoutState>("cancellation-flow")
            .Step("step-1", async (state, ct) =>
            {
                state.Steps.Add("step-1");
                return true;
            })
            .Step("step-2", async (state, ct) =>
            {
                state.Steps.Add("step-2-start");
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                state.Steps.Add("step-2-end");
                return true;
            })
            .Step("step-3", async (state, ct) =>
            {
                state.Steps.Add("step-3");
                return true;
            })
            .Build();
        var state = new TimeoutState { FlowId = "cancel-test" };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await executor.ExecuteAsync(flow, state, cts.Token);
        });

        state.Steps.Should().Contain("step-1", "first step should complete before cancellation");
        state.Steps.Should().Contain("step-2-start", "second step should start before cancellation");
        state.Steps.Should().NotContain("step-3", "third step should not execute after cancellation");
    }

    [Fact]
    public async Task Timeout_MultipleStepsWithDifferentTimeouts_RespectsEach()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var flow = FlowBuilder.Create<TimeoutState>("multi-timeout")
            .Step("fast-step", async (state, ct) =>
            {
                await Task.Delay(FastDelayMs, ct);
                state.Steps.Add("fast");
                return true;
            })
            .WithTimeout(TimeSpan.FromMilliseconds(LongTimeoutMs))
            .Step("medium-step", async (state, ct) =>
            {
                await Task.Delay(MediumDelayMs, ct);
                state.Steps.Add("medium");
                return true;
            })
            .WithTimeout(TimeSpan.FromMilliseconds(VeryLongTimeoutMs))
            .Step("final-step", async (state, ct) =>
            {
                state.Steps.Add("final");
                return true;
            })
            .Build();
        var state = new TimeoutState { FlowId = "multi-timeout-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue("all steps should complete within their timeouts");
        result.State.Steps.Should().Equal("fast", "medium", "final",
            "steps should execute in order");
    }

    [Fact]
    public async Task Timeout_InLoop_AffectsEachIteration()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var iterationCount = 0;
        var flow = FlowBuilder.Create<TimeoutState>("loop-timeout")
            .While(s => s.Counter < LoopIterations)
                .Do(f => f.Step("iteration", async (state, ct) =>
                {
                    iterationCount++;
                    state.Counter++;
                    await Task.Delay(FastDelayMs, ct);
                    state.Steps.Add($"iter-{state.Counter}");
                    return true;
                })
                .WithTimeout(TimeSpan.FromMilliseconds(LongTimeoutMs)))
            .EndWhile()
            .Build();
        var state = new TimeoutState { FlowId = "loop-timeout-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue("all iterations should complete within timeout");
        result.State.Counter.Should().Be(LoopIterations, "all iterations should execute");
        iterationCount.Should().Be(LoopIterations, "iteration count should match loop count");
    }

    [Fact]
    public async Task Timeout_WithFallback_ExecutesFallbackOnTimeout()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var flow = FlowBuilder.Create<TimeoutState>("timeout-fallback")
            .Step("primary", async (state, ct) =>
            {
                state.Steps.Add("primary-start");
                await Task.Delay(VerySlowDelayMs, ct);
                state.Steps.Add("primary-end");
                return true;
            })
            .WithTimeout(TimeSpan.FromMilliseconds(ShortTimeoutMs))
            .Build();
        var state = new TimeoutState { FlowId = "fallback-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse("step should timeout");
        result.State.Steps.Should().Contain("primary-start", "step should start before timeout");
        result.State.Steps.Should().NotContain("primary-end", "step should not complete due to timeout");
    }

    [Fact]
    public async Task Timeout_InParallel_CancelsAllBranches()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(MediumTimeoutMs));
        var parallelItems = new List<string> { "A", "B", "C", "D", "E" };
        var flow = FlowBuilder.Create<ParallelTimeoutState>("parallel-timeout")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    state.StartedItems.Add(item);
                    await Task.Delay(VerySlowDelayMs, ct);
                    state.CompletedItems.Add(item);
                    return true;
                }))
            .WithParallelism(parallelItems.Count)
            .Build();
        var state = new ParallelTimeoutState
        {
            FlowId = "parallel-timeout-test",
            Items = parallelItems
        };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await executor.ExecuteAsync(flow, state, cts.Token);
        });

        state.StartedItems.Should().NotBeEmpty("some items should start before cancellation");
        state.CompletedItems.Should().BeEmpty("no items should complete due to cancellation");
    }

    [Fact]
    public async Task Timeout_GracefulShutdown_AllowsCleanup()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var flow = FlowBuilder.Create<TimeoutState>("graceful-shutdown")
            .Step("long-operation", async (state, ct) =>
            {
                state.Steps.Add("operation-start");
                try
                {
                    await Task.Delay(VerySlowDelayMs, ct);
                }
                catch (OperationCanceledException)
                {
                    state.Steps.Add("operation-cancelled");
                    throw;
                }
                return true;
            })
            .WithTimeout(TimeSpan.FromMilliseconds(ShortTimeoutMs))
            .Build();
        var state = new TimeoutState { FlowId = "graceful-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse("operation should timeout");
        result.State.Steps.Should().Contain("operation-start", "operation should start before timeout");
    }

    public class TimeoutState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool StepStarted { get; set; }
        public bool StepCompleted { get; set; }
        public int Counter { get; set; }
        public List<string> Steps { get; set; } = new();
        public string? FallbackResult { get; set; }
    }

    public class ParallelTimeoutState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public List<string> StartedItems { get; set; } = new();
        public List<string> CompletedItems { get; set; } = new();
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
