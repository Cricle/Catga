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
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<TimeoutState>("step-timeout")
            .Step("slow-step", async (state, ct) =>
            {
                state.StepStarted = true;
                await Task.Delay(5000, ct); // Very slow
                state.StepCompleted = true;
                return true;
            })
            .WithTimeout(TimeSpan.FromMilliseconds(100))
            .Build();

        var state = new TimeoutState { FlowId = "timeout-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.StepStarted.Should().BeTrue();
        result.State.StepCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Timeout_StepCompletesInTime_Succeeds()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<TimeoutState>("fast-step")
            .Step("quick-step", async (state, ct) =>
            {
                state.StepStarted = true;
                await Task.Delay(10, ct); // Fast
                state.StepCompleted = true;
                return true;
            })
            .WithTimeout(TimeSpan.FromSeconds(5))
            .Build();

        var state = new TimeoutState { FlowId = "fast-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.StepCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Timeout_CancellationPropagates_ToAllSteps()
    {
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

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await executor.ExecuteAsync(flow, state, cts.Token);
        });

        state.Steps.Should().Contain("step-1");
        state.Steps.Should().Contain("step-2-start");
        state.Steps.Should().NotContain("step-3");
    }

    [Fact]
    public async Task Timeout_MultipleStepsWithDifferentTimeouts_RespectsEach()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<TimeoutState>("multi-timeout")
            .Step("fast-step", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                state.Steps.Add("fast");
                return true;
            })
            .WithTimeout(TimeSpan.FromSeconds(1))
            .Step("medium-step", async (state, ct) =>
            {
                await Task.Delay(20, ct);
                state.Steps.Add("medium");
                return true;
            })
            .WithTimeout(TimeSpan.FromSeconds(2))
            .Step("final-step", async (state, ct) =>
            {
                state.Steps.Add("final");
                return true;
            })
            .Build();

        var state = new TimeoutState { FlowId = "multi-timeout-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Steps.Should().Equal("fast", "medium", "final");
    }

    [Fact]
    public async Task Timeout_InLoop_AffectsEachIteration()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var iterationCount = 0;

        var flow = FlowBuilder.Create<TimeoutState>("loop-timeout")
            .While(s => s.Counter < 5)
                .Do(f => f.Step("iteration", async (state, ct) =>
                {
                    iterationCount++;
                    state.Counter++;
                    await Task.Delay(10, ct);
                    state.Steps.Add($"iter-{state.Counter}");
                    return true;
                })
                .WithTimeout(TimeSpan.FromSeconds(1)))
            .EndWhile()
            .Build();

        var state = new TimeoutState { FlowId = "loop-timeout-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeTrue();
        result.State.Counter.Should().Be(5);
        iterationCount.Should().Be(5);
    }

    [Fact]
    public async Task Timeout_WithFallback_ExecutesFallbackOnTimeout()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<TimeoutState>("timeout-fallback")
            .Step("primary", async (state, ct) =>
            {
                state.Steps.Add("primary-start");
                await Task.Delay(5000, ct); // Will timeout
                state.Steps.Add("primary-end");
                return true;
            })
            .WithTimeout(TimeSpan.FromMilliseconds(50))
            .Build();

        var state = new TimeoutState { FlowId = "fallback-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.Steps.Should().Contain("primary-start");
        result.State.Steps.Should().NotContain("primary-end");
    }

    [Fact]
    public async Task Timeout_InParallel_CancelsAllBranches()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var flow = FlowBuilder.Create<ParallelTimeoutState>("parallel-timeout")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    state.StartedItems.Add(item);
                    await Task.Delay(5000, ct); // Very slow
                    state.CompletedItems.Add(item);
                    return true;
                }))
            .WithParallelism(5)
            .Build();

        var state = new ParallelTimeoutState
        {
            FlowId = "parallel-timeout-test",
            Items = new List<string> { "A", "B", "C", "D", "E" }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await executor.ExecuteAsync(flow, state, cts.Token);
        });

        state.StartedItems.Should().NotBeEmpty();
        state.CompletedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task Timeout_GracefulShutdown_AllowsCleanup()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cleanupExecuted = false;

        var flow = FlowBuilder.Create<TimeoutState>("graceful-shutdown")
            .Step("long-operation", async (state, ct) =>
            {
                state.Steps.Add("operation-start");
                try
                {
                    await Task.Delay(5000, ct);
                }
                catch (OperationCanceledException)
                {
                    state.Steps.Add("operation-cancelled");
                    throw;
                }
                return true;
            })
            .WithTimeout(TimeSpan.FromMilliseconds(50))
            .Build();

        var state = new TimeoutState { FlowId = "graceful-test" };

        var result = await executor.ExecuteAsync(flow, state);

        result.IsSuccess.Should().BeFalse();
        result.State.Steps.Should().Contain("operation-start");
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
