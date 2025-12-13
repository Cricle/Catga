using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Long-running workflow scenario tests.
/// Tests workflows that span extended periods, checkpointing, and progress tracking.
/// </summary>
public class LongRunningFlowTests
{
    #region Test State

    public class LongRunningState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int TotalSteps { get; set; } = 10;
        public int CompletedSteps { get; set; }
        public List<string> CompletedStepNames { get; set; } = new();
        public Dictionary<string, DateTime> StepCompletionTimes { get; set; } = new();
        public double ProgressPercent => TotalSteps > 0 ? (double)CompletedSteps / TotalSteps * 100 : 0;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue ? CompletedAt - StartedAt : null;
        public List<Checkpoint> Checkpoints { get; set; } = new();
    }

    public record Checkpoint(string Name, int StepIndex, DateTime CreatedAt, Dictionary<string, object> Data);

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
    public async Task LongRunning_TracksProgress_UpdatesPercentage()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LongRunningState>("progress-tracking")
            .Step("start", async (state, ct) =>
            {
                state.StartedAt = DateTime.UtcNow;
                return true;
            })
            .ForEach(
                s => Enumerable.Range(1, s.TotalSteps).ToList(),
                (step, f) => f.Step($"step-{step}", async (state, ct) =>
                {
                    state.CompletedSteps++;
                    state.CompletedStepNames.Add($"step-{step}");
                    state.StepCompletionTimes[$"step-{step}"] = DateTime.UtcNow;
                    return true;
                }))
            .Step("complete", async (state, ct) =>
            {
                state.CompletedAt = DateTime.UtcNow;
                return true;
            })
            .Build();

        var state = new LongRunningState { FlowId = "progress-test", TotalSteps = 5 };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProgressPercent.Should().Be(100);
        result.State.CompletedSteps.Should().Be(5);
        result.State.Duration.Should().NotBeNull();
    }

    [Fact]
    public async Task LongRunning_CreatesCheckpoints_AtIntervals()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LongRunningState>("checkpoint-flow")
            .Step("phase-1", async (state, ct) =>
            {
                state.CompletedSteps++;
                state.Checkpoints.Add(new Checkpoint("phase-1-complete", state.CompletedSteps, DateTime.UtcNow, new() { ["data"] = "phase1" }));
                return true;
            })
            .Step("phase-2", async (state, ct) =>
            {
                state.CompletedSteps++;
                state.Checkpoints.Add(new Checkpoint("phase-2-complete", state.CompletedSteps, DateTime.UtcNow, new() { ["data"] = "phase2" }));
                return true;
            })
            .Step("phase-3", async (state, ct) =>
            {
                state.CompletedSteps++;
                state.Checkpoints.Add(new Checkpoint("phase-3-complete", state.CompletedSteps, DateTime.UtcNow, new() { ["data"] = "phase3" }));
                return true;
            })
            .Build();

        var state = new LongRunningState { FlowId = "checkpoint-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Checkpoints.Should().HaveCount(3);
        result.State.Checkpoints.Select(c => c.Name).Should()
            .ContainInOrder("phase-1-complete", "phase-2-complete", "phase-3-complete");
    }

    [Fact]
    public async Task LongRunning_WithDelays_CompletesWithinTimeout()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LongRunningState>("delayed-flow")
            .Step("step-1", async (state, ct) =>
            {
                await Task.Delay(50, ct);
                state.CompletedSteps++;
                return true;
            })
            .Step("step-2", async (state, ct) =>
            {
                await Task.Delay(50, ct);
                state.CompletedSteps++;
                return true;
            })
            .Step("step-3", async (state, ct) =>
            {
                await Task.Delay(50, ct);
                state.CompletedSteps++;
                return true;
            })
            .Build();

        var state = new LongRunningState { FlowId = "delay-test", StartedAt = DateTime.UtcNow };

        // Act
        var result = await executor.ExecuteAsync(flow, state);
        state.CompletedAt = DateTime.UtcNow;

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CompletedSteps.Should().Be(3);
        state.Duration!.Value.TotalMilliseconds.Should().BeGreaterThan(150);
    }

    [Fact]
    public async Task LongRunning_BatchProcessing_HandlesLargeDataset()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<BatchProcessState>("batch-processing")
            .Step("init", async (state, ct) =>
            {
                state.StartedAt = DateTime.UtcNow;
                state.TotalItems = state.Items.Count;
                return true;
            })
            .While(s => s.ProcessedBatches * s.BatchSize < s.TotalItems)
                .Do(f => f.Step("process-batch", async (state, ct) =>
                {
                    var skip = state.ProcessedBatches * state.BatchSize;
                    var batch = state.Items.Skip(skip).Take(state.BatchSize).ToList();

                    foreach (var item in batch)
                    {
                        state.ProcessedItems.Add($"{item}-done");
                    }

                    state.ProcessedBatches++;
                    state.Checkpoints.Add(new Checkpoint(
                        $"batch-{state.ProcessedBatches}",
                        state.ProcessedBatches,
                        DateTime.UtcNow,
                        new() { ["batchSize"] = batch.Count }));

                    return true;
                }))
            .EndWhile()
            .Step("finalize", async (state, ct) =>
            {
                state.CompletedAt = DateTime.UtcNow;
                return true;
            })
            .Build();

        var state = new BatchProcessState
        {
            FlowId = "batch-test",
            BatchSize = 10,
            Items = Enumerable.Range(1, 35).Select(i => $"item-{i}").ToList()
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(35);
        result.State.ProcessedBatches.Should().Be(4); // 35 items / 10 per batch = 4 batches
        result.State.Checkpoints.Should().HaveCount(4);
    }

    [Fact]
    public async Task LongRunning_MultiPhase_ExecutesInOrder()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<MultiPhaseState>("multi-phase")
            // Phase 1: Initialization
            .Step("init-phase", async (state, ct) =>
            {
                state.CurrentPhase = "Initialization";
                state.PhaseLog.Add("Initialization started");
                await Task.Delay(20, ct);
                state.PhaseLog.Add("Initialization completed");
                return true;
            })
            // Phase 2: Data Collection
            .Step("collect-phase", async (state, ct) =>
            {
                state.CurrentPhase = "DataCollection";
                state.PhaseLog.Add("Data collection started");
                for (int i = 0; i < 5; i++)
                {
                    state.CollectedData.Add($"data-{i}");
                }
                state.PhaseLog.Add($"Collected {state.CollectedData.Count} items");
                return true;
            })
            // Phase 3: Processing
            .Step("process-phase", async (state, ct) =>
            {
                state.CurrentPhase = "Processing";
                state.PhaseLog.Add("Processing started");
                foreach (var data in state.CollectedData)
                {
                    state.ProcessedData.Add($"{data}-processed");
                }
                state.PhaseLog.Add($"Processed {state.ProcessedData.Count} items");
                return true;
            })
            // Phase 4: Validation
            .Step("validate-phase", async (state, ct) =>
            {
                state.CurrentPhase = "Validation";
                state.PhaseLog.Add("Validation started");
                state.IsValid = state.ProcessedData.Count == state.CollectedData.Count;
                state.PhaseLog.Add($"Validation result: {state.IsValid}");
                return true;
            })
            // Phase 5: Completion
            .Step("complete-phase", async (state, ct) =>
            {
                state.CurrentPhase = "Completed";
                state.PhaseLog.Add("Workflow completed");
                return true;
            })
            .Build();

        var state = new MultiPhaseState { FlowId = "phase-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CurrentPhase.Should().Be("Completed");
        result.State.IsValid.Should().BeTrue();
        result.State.PhaseLog.Should().HaveCount(8);
    }

    [Fact]
    public async Task LongRunning_WithRetryableSteps_RecoversFromFailures()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var attemptCounts = new Dictionary<string, int>();

        var flow = FlowBuilder.Create<LongRunningState>("retryable-steps")
            .Step("reliable-step", async (state, ct) =>
            {
                state.CompletedStepNames.Add("reliable");
                return true;
            })
            .Step("flaky-step", async (state, ct) =>
            {
                var key = "flaky";
                attemptCounts.TryGetValue(key, out var count);
                attemptCounts[key] = ++count;

                if (count < 3)
                {
                    throw new InvalidOperationException($"Transient failure {count}");
                }

                state.CompletedStepNames.Add("flaky");
                return true;
            })
            .WithRetry(5, TimeSpan.FromMilliseconds(10))
            .Step("final-step", async (state, ct) =>
            {
                state.CompletedStepNames.Add("final");
                return true;
            })
            .Build();

        var state = new LongRunningState { FlowId = "retry-test" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CompletedStepNames.Should().ContainInOrder("reliable", "flaky", "final");
        attemptCounts["flaky"].Should().Be(3);
    }

    public class BatchProcessState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public int TotalItems { get; set; }
        public int BatchSize { get; set; } = 10;
        public int ProcessedBatches { get; set; }
        public List<string> ProcessedItems { get; set; } = new();
        public List<Checkpoint> Checkpoints { get; set; } = new();
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public class MultiPhaseState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string CurrentPhase { get; set; } = "";
        public List<string> PhaseLog { get; set; } = new();
        public List<string> CollectedData { get; set; } = new();
        public List<string> ProcessedData { get; set; } = new();
        public bool IsValid { get; set; }
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
