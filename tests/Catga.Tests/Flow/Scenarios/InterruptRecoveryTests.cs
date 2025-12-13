using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Interrupt and recovery scenario tests.
/// Tests flow persistence, resume after failure, checkpoint recovery, and state restoration.
/// </summary>
public class InterruptRecoveryTests
{
    #region Test State

    public class RecoverableState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int CurrentStep { get; set; }
        public int TotalSteps { get; set; } = 5;
        public List<string> CompletedSteps { get; set; } = new();
        public List<string> RecoveredSteps { get; set; } = new();
        public bool WasInterrupted { get; set; }
        public int InterruptAtStep { get; set; } = -1;
        public int ResumeCount { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public Dictionary<string, object> Checkpoints { get; set; } = new();
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
    public async Task Recovery_FlowFailsAndResumes_ContinuesFromFailurePoint()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var flowStore = sp.GetRequiredService<IDslFlowStore>();
        var shouldFail = true;

        var flow = FlowBuilder.Create<RecoverableState>("resumable-flow")
            .Step("step-1", async (state, ct) =>
            {
                state.CompletedSteps.Add("step-1");
                state.CurrentStep = 1;
                return true;
            })
            .Step("step-2", async (state, ct) =>
            {
                state.CompletedSteps.Add("step-2");
                state.CurrentStep = 2;
                return true;
            })
            .Step("step-3-may-fail", async (state, ct) =>
            {
                if (shouldFail)
                {
                    shouldFail = false;
                    state.WasInterrupted = true;
                    throw new InvalidOperationException("Simulated failure");
                }
                state.CompletedSteps.Add("step-3");
                state.CurrentStep = 3;
                return true;
            })
            .Step("step-4", async (state, ct) =>
            {
                state.CompletedSteps.Add("step-4");
                state.CurrentStep = 4;
                return true;
            })
            .Step("step-5", async (state, ct) =>
            {
                state.CompletedSteps.Add("step-5");
                state.CurrentStep = 5;
                state.CompletedAt = DateTime.UtcNow;
                return true;
            })
            .Build();

        var initialState = new RecoverableState
        {
            FlowId = $"recovery-{Guid.NewGuid():N}",
            StartedAt = DateTime.UtcNow
        };

        // Act 1 - First execution (fails at step 3)
        var result1 = await executor.ExecuteAsync(flow, initialState);

        // Assert 1
        result1.IsSuccess.Should().BeFalse();
        result1.State.WasInterrupted.Should().BeTrue();
        result1.State.CompletedSteps.Should().Contain(new[] { "step-1", "step-2" });
        result1.State.CompletedSteps.Should().NotContain("step-3");

        // Act 2 - Resume execution
        result1.State.ResumeCount++;
        var result2 = await executor.ResumeAsync(flow, initialState.FlowId);

        // Assert 2
        result2.IsSuccess.Should().BeTrue();
        result2.State.CompletedSteps.Should().Contain("step-3");
        result2.State.CompletedSteps.Should().Contain("step-4");
        result2.State.CompletedSteps.Should().Contain("step-5");
        result2.State.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Recovery_MultipleFailuresAndResumes_EventuallyCompletes()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var failureCount = 0;
        var maxFailures = 3;

        var flow = FlowBuilder.Create<RecoverableState>("multi-failure-flow")
            .Step("step-1", async (state, ct) =>
            {
                state.CompletedSteps.Add("step-1");
                return true;
            })
            .Step("unreliable-step", async (state, ct) =>
            {
                failureCount++;
                if (failureCount <= maxFailures)
                {
                    state.WasInterrupted = true;
                    throw new InvalidOperationException($"Failure {failureCount}");
                }
                state.CompletedSteps.Add("unreliable-step");
                return true;
            })
            .Step("final-step", async (state, ct) =>
            {
                state.CompletedSteps.Add("final-step");
                return true;
            })
            .Build();

        var state = new RecoverableState { FlowId = $"multi-fail-{Guid.NewGuid():N}" };

        // Act - Execute and resume multiple times
        FlowResult<RecoverableState>? result = null;
        for (int i = 0; i <= maxFailures; i++)
        {
            if (i == 0)
            {
                result = await executor.ExecuteAsync(flow, state);
            }
            else
            {
                state.ResumeCount++;
                result = await executor.ResumeAsync(flow, state.FlowId);
            }

            if (result.IsSuccess) break;
        }

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue();
        result.State.CompletedSteps.Should().Contain("final-step");
        failureCount.Should().Be(maxFailures + 1);
    }

    [Fact]
    public async Task Recovery_WithCheckpoints_RestoresIntermediateState()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var shouldFail = true;

        var flow = FlowBuilder.Create<RecoverableState>("checkpoint-flow")
            .Step("process-batch-1", async (state, ct) =>
            {
                state.Checkpoints["batch1"] = new { processed = 100, status = "complete" };
                state.CompletedSteps.Add("batch-1");
                return true;
            })
            .Step("process-batch-2", async (state, ct) =>
            {
                state.Checkpoints["batch2"] = new { processed = 200, status = "complete" };
                state.CompletedSteps.Add("batch-2");
                return true;
            })
            .Step("process-batch-3", async (state, ct) =>
            {
                if (shouldFail)
                {
                    shouldFail = false;
                    state.Checkpoints["batch3"] = new { processed = 50, status = "partial" };
                    throw new InvalidOperationException("Interrupted during batch 3");
                }
                state.Checkpoints["batch3"] = new { processed = 300, status = "complete" };
                state.CompletedSteps.Add("batch-3");
                return true;
            })
            .Step("finalize", async (state, ct) =>
            {
                state.CompletedSteps.Add("finalize");
                return true;
            })
            .Build();

        var state = new RecoverableState { FlowId = $"checkpoint-{Guid.NewGuid():N}" };

        // Act 1 - First execution (fails)
        var result1 = await executor.ExecuteAsync(flow, state);

        // Assert 1 - Checkpoints preserved
        result1.IsSuccess.Should().BeFalse();
        result1.State.Checkpoints.Should().ContainKey("batch1");
        result1.State.Checkpoints.Should().ContainKey("batch2");
        result1.State.Checkpoints.Should().ContainKey("batch3");

        // Act 2 - Resume
        var result2 = await executor.ResumeAsync(flow, state.FlowId);

        // Assert 2
        result2.IsSuccess.Should().BeTrue();
        result2.State.CompletedSteps.Should().Contain("batch-3");
        result2.State.CompletedSteps.Should().Contain("finalize");
    }

    [Fact]
    public async Task Recovery_WithCompensation_RollsBackOnFailure()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var compensationLog = new List<string>();

        var flow = FlowBuilder.Create<RecoverableState>("compensation-recovery")
            .Step("action-1", async (state, ct) =>
            {
                state.CompletedSteps.Add("action-1");
                state.Checkpoints["action1"] = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                compensationLog.Add("rollback-1");
                state.Checkpoints["action1"] = false;
            })
            .Step("action-2", async (state, ct) =>
            {
                state.CompletedSteps.Add("action-2");
                state.Checkpoints["action2"] = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                compensationLog.Add("rollback-2");
                state.Checkpoints["action2"] = false;
            })
            .Step("action-3-fail", async (state, ct) =>
            {
                throw new InvalidOperationException("Critical failure");
            })
            .Build();

        var state = new RecoverableState { FlowId = $"comp-recovery-{Guid.NewGuid():N}" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        compensationLog.Should().Contain("rollback-2");
        compensationLog.Should().Contain("rollback-1");
        // Verify compensation order (reverse)
        compensationLog.IndexOf("rollback-2").Should().BeLessThan(compensationLog.IndexOf("rollback-1"));
    }

    [Fact]
    public async Task Recovery_CancellationDuringExecution_HandlesGracefully()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cts = new CancellationTokenSource();

        var flow = FlowBuilder.Create<RecoverableState>("cancellable-flow")
            .Step("step-1", async (state, ct) =>
            {
                state.CompletedSteps.Add("step-1");
                return true;
            })
            .Step("step-2-slow", async (state, ct) =>
            {
                // Simulate long operation
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                state.CompletedSteps.Add("step-2");
                return true;
            })
            .Step("step-3", async (state, ct) =>
            {
                state.CompletedSteps.Add("step-3");
                return true;
            })
            .Build();

        var state = new RecoverableState { FlowId = $"cancel-{Guid.NewGuid():N}" };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await executor.ExecuteAsync(flow, state, cts.Token);
        });

        // Verify partial completion
        state.CompletedSteps.Should().Contain("step-1");
        state.CompletedSteps.Should().NotContain("step-2");
    }

    [Fact]
    public async Task Recovery_LoopInterruptAndResume_ContinuesFromLastIteration()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var iteration = 0;
        var shouldFail = true;

        var flow = FlowBuilder.Create<RecoverableState>("loop-recovery")
            .While(s => s.CurrentStep < 5)
                .Do(f => f.Step("iterate", async (state, ct) =>
                {
                    iteration++;
                    state.CurrentStep++;
                    state.CompletedSteps.Add($"iteration-{state.CurrentStep}");

                    // Fail at iteration 3
                    if (state.CurrentStep == 3 && shouldFail)
                    {
                        shouldFail = false;
                        throw new InvalidOperationException("Loop interrupted");
                    }
                    return true;
                }))
            .EndWhile()
            .Step("complete", async (state, ct) =>
            {
                state.CompletedSteps.Add("complete");
                return true;
            })
            .Build();

        var state = new RecoverableState { FlowId = $"loop-{Guid.NewGuid():N}" };

        // Act 1 - Execute until failure
        var result1 = await executor.ExecuteAsync(flow, state);
        result1.IsSuccess.Should().BeFalse();

        // Act 2 - Resume
        var result2 = await executor.ResumeAsync(flow, state.FlowId);

        // Assert
        result2.IsSuccess.Should().BeTrue();
        result2.State.CurrentStep.Should().Be(5);
        result2.State.CompletedSteps.Should().Contain("complete");
    }

    [Fact]
    public async Task Recovery_BranchInterruptAndResume_ContinuesCorrectBranch()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var shouldFail = true;

        var flow = FlowBuilder.Create<RecoverableState>("branch-recovery")
            .Step("init", async (state, ct) =>
            {
                state.CompletedSteps.Add("init");
                return true;
            })
            .If(s => s.TotalSteps > 3)
                .Then(f => f
                    .Step("branch-step-1", async (state, ct) =>
                    {
                        state.CompletedSteps.Add("branch-1");
                        return true;
                    })
                    .Step("branch-step-2-may-fail", async (state, ct) =>
                    {
                        if (shouldFail)
                        {
                            shouldFail = false;
                            throw new InvalidOperationException("Branch interrupted");
                        }
                        state.CompletedSteps.Add("branch-2");
                        return true;
                    })
                    .Step("branch-step-3", async (state, ct) =>
                    {
                        state.CompletedSteps.Add("branch-3");
                        return true;
                    }))
            .EndIf()
            .Step("finalize", async (state, ct) =>
            {
                state.CompletedSteps.Add("finalize");
                return true;
            })
            .Build();

        var state = new RecoverableState { FlowId = $"branch-{Guid.NewGuid():N}", TotalSteps = 5 };

        // Act 1 - Execute until failure in branch
        var result1 = await executor.ExecuteAsync(flow, state);
        result1.IsSuccess.Should().BeFalse();
        result1.State.CompletedSteps.Should().Contain("init");
        result1.State.CompletedSteps.Should().Contain("branch-1");
        result1.State.CompletedSteps.Should().NotContain("branch-2");

        // Act 2 - Resume
        var result2 = await executor.ResumeAsync(flow, state.FlowId);

        // Assert
        result2.IsSuccess.Should().BeTrue();
        result2.State.CompletedSteps.Should().Contain("branch-2");
        result2.State.CompletedSteps.Should().Contain("branch-3");
        result2.State.CompletedSteps.Should().Contain("finalize");
    }

    [Fact]
    public async Task Recovery_ForEachInterruptAndResume_ContinuesFromLastItem()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var processedItems = new List<string>();
        var failAtItem = "item-3";
        var shouldFail = true;

        var flow = FlowBuilder.Create<ItemProcessingState>("foreach-recovery")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    if (item == failAtItem && shouldFail)
                    {
                        shouldFail = false;
                        throw new InvalidOperationException($"Failed at {item}");
                    }
                    processedItems.Add(item);
                    state.ProcessedItems.Add(item);
                    return true;
                }))
            .Step("complete", async (state, ct) =>
            {
                state.Completed = true;
                return true;
            })
            .Build();

        var state = new ItemProcessingState
        {
            FlowId = $"foreach-{Guid.NewGuid():N}",
            Items = new List<string> { "item-1", "item-2", "item-3", "item-4", "item-5" }
        };

        // Act 1 - Execute until failure
        var result1 = await executor.ExecuteAsync(flow, state);
        result1.IsSuccess.Should().BeFalse();

        // Act 2 - Resume
        processedItems.Clear();
        var result2 = await executor.ResumeAsync(flow, state.FlowId);

        // Assert
        result2.IsSuccess.Should().BeTrue();
        result2.State.Completed.Should().BeTrue();
        result2.State.ProcessedItems.Should().HaveCount(5);
    }

    [Fact]
    public async Task Recovery_StatePersistedAcrossResumes_MaintainsData()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var flowStore = sp.GetRequiredService<IDslFlowStore>();
        var shouldFail = true;

        var flow = FlowBuilder.Create<RecoverableState>("state-persistence")
            .Step("set-data", async (state, ct) =>
            {
                state.Checkpoints["key1"] = "value1";
                state.Checkpoints["key2"] = 42;
                state.CompletedSteps.Add("set-data");
                return true;
            })
            .Step("may-fail", async (state, ct) =>
            {
                if (shouldFail)
                {
                    shouldFail = false;
                    throw new InvalidOperationException("Fail");
                }
                state.CompletedSteps.Add("may-fail");
                return true;
            })
            .Step("verify-data", async (state, ct) =>
            {
                // Data should still be present after resume
                if (!state.Checkpoints.ContainsKey("key1"))
                {
                    throw new InvalidOperationException("Data lost!");
                }
                state.CompletedSteps.Add("verify-data");
                return true;
            })
            .Build();

        var state = new RecoverableState { FlowId = $"persist-{Guid.NewGuid():N}" };

        // Act
        var result1 = await executor.ExecuteAsync(flow, state);
        result1.IsSuccess.Should().BeFalse();

        var result2 = await executor.ResumeAsync(flow, state.FlowId);

        // Assert
        result2.IsSuccess.Should().BeTrue();
        result2.State.Checkpoints.Should().ContainKey("key1");
        result2.State.Checkpoints.Should().ContainKey("key2");
        result2.State.CompletedSteps.Should().Contain("verify-data");
    }

    public class ItemProcessingState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public List<string> ProcessedItems { get; set; } = new();
        public bool Completed { get; set; }
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
