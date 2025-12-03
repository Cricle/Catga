using Catga.Core;
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
            .Step(async () => { steps.Add(1); await Task.Delay(1); return "r1"; })
            .Step(async () => { steps.Add(2); await Task.Delay(1); return "r2"; })
            .Step(async () => { steps.Add(3); await Task.Delay(1); return "r3"; })
            .ExecuteAsync<string>();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("r3");
        result.CompletedSteps.Should().Be(3);
        steps.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task Flow_StepFails_CompensatesInReverseOrder()
    {
        var compensated = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async () => { await Task.Delay(1); return "r1"; },
                async (string _) => { compensated.Add(1); await Task.Delay(1); })
            .Step(async () => { await Task.Delay(1); return "r2"; },
                async (string _) => { compensated.Add(2); await Task.Delay(1); })
            .Step(async () => { await Task.Delay(1); throw new Exception("fail"); return "r3"; })
            .ExecuteAsync<string>();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("fail");
        compensated.Should().BeEquivalentTo([2, 1]); // Reverse order
    }

    [Fact]
    public async Task Flow_WithCatgaResult_HandlesFailure()
    {
        var compensated = new List<int>();

        var result = await Flow.Create("Test")
            .Step<string>(async () => { await Task.Delay(1); return CatgaResult<string>.Success("ok"); },
                async _ => { compensated.Add(1); await Task.Delay(1); })
            .Step<string>(async () => { await Task.Delay(1); return CatgaResult<string>.Failure("error"); })
            .ExecuteAsync<string>();

        result.IsSuccess.Should().BeFalse();
        compensated.Should().BeEquivalentTo([1]);
    }

    [Fact]
    public async Task Flow_VoidSteps_Work()
    {
        var executed = new List<int>();

        var result = await Flow.Create("Test")
            .Step(async () => { executed.Add(1); await Task.Delay(1); })
            .Step(async () => { executed.Add(2); await Task.Delay(1); })
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
            .Step(async () => { await Task.Delay(1); return "r1"; },
                async _ => { compensated.Add(1); await Task.Delay(1); })
            .Step(async () => { cts.Cancel(); await Task.Delay(100, cts.Token); return "r2"; })
            .ExecuteAsync<string>(cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.IsCancelled.Should().BeTrue();
        compensated.Should().Contain(1);
    }

    [Fact]
    public async Task Flow_Duration_IsTracked()
    {
        var result = await Flow.Create("Test")
            .Step(async () => { await Task.Delay(50); return "r1"; })
            .ExecuteAsync<string>();

        result.Duration.TotalMilliseconds.Should().BeGreaterThan(40);
    }
}
