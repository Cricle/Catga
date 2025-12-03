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
}
