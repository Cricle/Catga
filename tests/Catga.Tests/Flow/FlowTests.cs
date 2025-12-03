using FluentAssertions;

namespace Catga.Tests.Flow;

using Flow = Catga.Flow.Flow;

public class FlowTests
{
    [Fact]
    public async Task Flow_AllStepsSucceed_ReturnsSuccess()
    {
        var steps = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { steps.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { steps.Add(2); await Task.Delay(1, ct); })
            .Step(async ct => { steps.Add(3); await Task.Delay(1, ct); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(3);
        steps.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task Flow_StepFails_CompensatesInReverseOrder()
    {
        var compensated = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(2); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); throw new Exception("fail"); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("fail");
        compensated.Should().BeEquivalentTo([2, 1]); // Reverse order
    }

    [Fact]
    public async Task Flow_VoidSteps_Work()
    {
        var executed = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { executed.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { executed.Add(2); await Task.Delay(1, ct); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(2);
        executed.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public async Task Flow_Cancellation_Compensates()
    {
        var compensated = new List<int>();
        using var cts = new CancellationTokenSource();

        var result = await Flow.Create("Test")
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { cts.Cancel(); await Task.Delay(100, ct); })
            .ExecuteAsync(cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.IsCancelled.Should().BeTrue();
        compensated.Should().Contain(1);
    }

    [Fact]
    public async Task Flow_Duration_IsTracked()
    {
        var result = await Flow.Create("Test")
            .Step(async ct => { await Task.Delay(50, ct); })
            .ExecuteAsync();

        result.Duration.TotalMilliseconds.Should().BeGreaterThan(40);
    }

    [Fact]
    public async Task Flow_ExecuteFromStep_ResumesCorrectly()
    {
        var executed = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { executed.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { executed.Add(2); await Task.Delay(1, ct); })
            .Step(async ct => { executed.Add(3); await Task.Delay(1, ct); })
            .ExecuteFromAsync(1); // Start from step 2

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(3);
        executed.Should().BeEquivalentTo([2, 3]); // Only steps 2 and 3 executed
    }

    [Fact]
    public async Task Flow_EmptyFlow_Succeeds()
    {
        var result = await Flow.Create("Empty").ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(0);
    }

    [Fact]
    public async Task Flow_CompensationFails_ContinuesCompensating()
    {
        var compensated = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); },
                ct => { compensated.Add(2); throw new Exception("comp fail"); })
            .Step(async ct => { await Task.Delay(1, ct); throw new Exception("step fail"); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        // Both compensations should be attempted even if one fails
        compensated.Should().Contain(2);
        compensated.Should().Contain(1);
    }

    [Fact]
    public async Task Flow_NoCompensation_StillFails()
    {
        var result = await Flow.Create("Test")
            .Step(async ct => { await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); throw new Exception("fail"); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        result.CompletedSteps.Should().Be(1);
    }

    [Fact]
    public async Task Flow_WithoutCancellationToken_Works()
    {
        var executed = false;

        var result = await Flow.Create("Test")
            .Step(async () => { executed = true; await Task.Delay(1); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task Flow_MixedStepTypes_Work()
    {
        var steps = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { steps.Add(1); await Task.Delay(1, ct); })
            .Step(async () => { steps.Add(2); await Task.Delay(1); })
            .Step(async ct => { steps.Add(3); await Task.Delay(1, ct); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        steps.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task Flow_FirstStepFails_NoCompensation()
    {
        var compensated = new List<int>();

        var result = await Flow.Create("Test")
            .Step(ct => throw new Exception("first fail"),
                async ct => { compensated.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(2); await Task.Delay(1, ct); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        result.CompletedSteps.Should().Be(0);
        compensated.Should().BeEmpty(); // No steps completed, no compensation
    }

    [Fact]
    public async Task Flow_ExecuteFromAsync_WithCompensation()
    {
        var compensated = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(2); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); throw new Exception("fail"); })
            .ExecuteFromAsync(1); // Start from step index 1 (second step)

        result.IsSuccess.Should().BeFalse();
        // Step index 1 was executed successfully, then step index 2 failed
        // Only step index 1 should compensate (step 1 was skipped)
        compensated.Should().BeEquivalentTo([2]);
    }

    [Fact]
    public async Task Flow_ExecuteFromAsync_SkipsEarlierSteps()
    {
        var executed = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { executed.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { executed.Add(2); await Task.Delay(1, ct); })
            .Step(async ct => { executed.Add(3); await Task.Delay(1, ct); })
            .ExecuteFromAsync(2); // Start from step index 2 (third step)

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeEquivalentTo([3]); // Only step 3 executed
    }

    #region TDD: Edge Cases

    [Fact]
    public async Task Flow_AllStepsHaveCompensation_AllCompensate()
    {
        var compensated = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(2); await Task.Delay(1, ct); })
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(3); await Task.Delay(1, ct); })
            .Step(async ct => { throw new Exception("fail"); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        compensated.Should().BeEquivalentTo([3, 2, 1]); // All compensate in reverse
    }

    [Fact]
    public async Task Flow_LastStepFails_AllPreviousCompensate()
    {
        var executed = new List<int>();
        var compensated = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { executed.Add(1); await Task.Delay(1, ct); },
                async ct => { compensated.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { executed.Add(2); await Task.Delay(1, ct); },
                async ct => { compensated.Add(2); await Task.Delay(1, ct); })
            .Step(async ct => { executed.Add(3); throw new Exception("last fail"); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        executed.Should().BeEquivalentTo([1, 2, 3]);
        compensated.Should().BeEquivalentTo([2, 1]);
    }

    [Fact]
    public async Task Flow_MiddleStepFails_OnlyPreviousCompensate()
    {
        var executed = new List<int>();
        var compensated = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { executed.Add(1); await Task.Delay(1, ct); },
                async ct => { compensated.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { executed.Add(2); throw new Exception("middle fail"); },
                async ct => { compensated.Add(2); await Task.Delay(1, ct); })
            .Step(async ct => { executed.Add(3); await Task.Delay(1, ct); },
                async ct => { compensated.Add(3); await Task.Delay(1, ct); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        executed.Should().BeEquivalentTo([1, 2]);
        compensated.Should().BeEquivalentTo([1]); // Only step 1 compensates
    }

    [Fact]
    public async Task Flow_ExecuteFromAsync_OutOfRange_ExecutesNothing()
    {
        var executed = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { executed.Add(1); await Task.Delay(1, ct); })
            .Step(async ct => { executed.Add(2); await Task.Delay(1, ct); })
            .ExecuteFromAsync(10); // Beyond step count

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(10); // Starts at 10, no steps executed
        executed.Should().BeEmpty();
    }

    [Fact]
    public async Task Flow_CancellationBeforeFirstStep_NoExecution()
    {
        var executed = new List<int>();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel before execution

        var result = await Flow.Create("Test")
            .Step(async ct => { executed.Add(1); await Task.Delay(1, ct); })
            .ExecuteAsync(cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.IsCancelled.Should().BeTrue();
        executed.Should().BeEmpty();
    }

    [Fact]
    public async Task Flow_SyncException_HandledCorrectly()
    {
        var result = await Flow.Create("Test")
            .Step(ct => throw new InvalidOperationException("sync error"))
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("sync error");
    }

    [Fact]
    public async Task Flow_NullCompensation_Skipped()
    {
        var compensated = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async ct => { await Task.Delay(1, ct); }, null)
            .Step(async ct => { await Task.Delay(1, ct); },
                async ct => { compensated.Add(2); await Task.Delay(1, ct); })
            .Step(async ct => { throw new Exception("fail"); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        compensated.Should().BeEquivalentTo([2]); // Only step 2 has compensation
    }

    [Fact]
    public async Task Flow_LongRunningStep_CompletesSuccessfully()
    {
        var result = await Flow.Create("Test")
            .Step(async ct => { await Task.Delay(100, ct); })
            .ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        result.Duration.TotalMilliseconds.Should().BeGreaterThan(90);
    }

    [Fact]
    public async Task Flow_MultipleFlowsParallel_Independent()
    {
        var flow1Steps = new List<int>();
        var flow2Steps = new List<int>();

        var task1 = Flow.Create("Flow1")
            .Step(async ct => { flow1Steps.Add(1); await Task.Delay(50, ct); })
            .Step(async ct => { flow1Steps.Add(2); await Task.Delay(50, ct); })
            .ExecuteAsync();

        var task2 = Flow.Create("Flow2")
            .Step(async ct => { flow2Steps.Add(1); await Task.Delay(50, ct); })
            .Step(async ct => { flow2Steps.Add(2); await Task.Delay(50, ct); })
            .ExecuteAsync();

        var results = await Task.WhenAll(task1, task2);

        results[0].IsSuccess.Should().BeTrue();
        results[1].IsSuccess.Should().BeTrue();
        flow1Steps.Should().BeEquivalentTo([1, 2]);
        flow2Steps.Should().BeEquivalentTo([1, 2]);
    }

    #endregion

    #region TDD: Stress Tests

    [Fact]
    public async Task Flow_ManySteps_ExecutesAll()
    {
        var executed = new List<int>();
        var flow = Flow.Create("ManySteps");

        for (int i = 1; i <= 100; i++)
        {
            var step = i;
            flow.Step(async ct => { executed.Add(step); await Task.Yield(); });
        }

        var result = await flow.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(100);
        executed.Should().HaveCount(100);
    }

    [Fact]
    public async Task Flow_ManyStepsWithCompensation_AllCompensate()
    {
        var compensated = new List<int>();
        var flow = Flow.Create("ManySteps");

        for (int i = 1; i <= 50; i++)
        {
            var step = i;
            flow.Step(
                async ct => { await Task.Yield(); },
                async ct => { compensated.Add(step); await Task.Yield(); });
        }
        flow.Step(ct => throw new Exception("fail"));

        var result = await flow.ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        compensated.Should().HaveCount(50);
        // Verify reverse order
        for (int i = 0; i < 50; i++)
        {
            compensated[i].Should().Be(50 - i);
        }
    }

    #endregion
}
