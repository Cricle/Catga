using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Flow;

using Flow = Catga.Flow.Flow;

public class FlowTests
{
    [Fact]
    public async Task Flow_AllStepsSucceed_ShouldReturnSuccess()
    {
        // Arrange
        var steps = new List<string>();

        // Act
        var result = await Flow.Create("TestFlow")
            .Step("Step1", async () => { steps.Add("Step1"); await Task.Delay(1); return "result1"; })
            .Step("Step2", async () => { steps.Add("Step2"); await Task.Delay(1); return "result2"; })
            .Step("Step3", async () => { steps.Add("Step3"); await Task.Delay(1); return "result3"; })
            .ExecuteAsync<string>();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("result3");
        result.CompletedSteps.Should().Be(3);
        steps.Should().BeEquivalentTo(["Step1", "Step2", "Step3"]);
    }

    [Fact]
    public async Task Flow_StepFails_ShouldCompensateInReverseOrder()
    {
        // Arrange
        var executed = new List<string>();
        var compensated = new List<string>();

        // Act
        var result = await Flow.Create("TestFlow")
            .Step("Step1",
                async () => { executed.Add("Step1"); await Task.Delay(1); return "r1"; },
                async _ => { compensated.Add("Comp1"); await Task.Delay(1); })
            .Step("Step2",
                async () => { executed.Add("Step2"); await Task.Delay(1); return "r2"; },
                async _ => { compensated.Add("Comp2"); await Task.Delay(1); })
            .Step("Step3",
                async () => { executed.Add("Step3"); throw new Exception("Step3 failed"); return "r3"; },
                async _ => { compensated.Add("Comp3"); await Task.Delay(1); })
            .ExecuteAsync<string>();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailedStep.Should().Be("Step3");
        result.Error.Should().Contain("Step3 failed");
        executed.Should().BeEquivalentTo(["Step1", "Step2", "Step3"]);
        compensated.Should().BeEquivalentTo(["Comp2", "Comp1"]); // Reverse order, Step3 not compensated (failed)
    }

    [Fact]
    public async Task Flow_WithCatgaResult_ShouldHandleFailure()
    {
        // Arrange
        var compensated = new List<string>();

        // Act
        var result = await Flow.Create("TestFlow")
            .Step("Step1",
                () => Task.FromResult(CatgaResult<string>.Success("ok")),
                async _ => { compensated.Add("Comp1"); await Task.Delay(1); })
            .Step("Step2",
                () => Task.FromResult(CatgaResult<string>.Failure("Step2 error")),
                async _ => { compensated.Add("Comp2"); await Task.Delay(1); })
            .ExecuteAsync<string>();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailedStep.Should().Be("Step2");
        result.Error.Should().Contain("Step2 error");
        compensated.Should().BeEquivalentTo(["Comp1"]); // Only Step1 compensated
    }

    [Fact]
    public async Task Flow_VoidSteps_ShouldWork()
    {
        // Arrange
        var executed = new List<string>();

        // Act
        var result = await Flow.Create("TestFlow")
            .Step("Step1", async () => { executed.Add("Step1"); await Task.Delay(1); })
            .Step("Step2", async () => { executed.Add("Step2"); await Task.Delay(1); })
            .ExecuteAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(2);
        executed.Should().BeEquivalentTo(["Step1", "Step2"]);
    }

    [Fact]
    public async Task Flow_Cancellation_ShouldCompensate()
    {
        // Arrange
        var compensated = new List<string>();
        using var cts = new CancellationTokenSource();

        // Act
        var result = await Flow.Create("TestFlow")
            .Step("Step1",
                async () => { await Task.Delay(1); return "r1"; },
                async _ => { compensated.Add("Comp1"); await Task.Delay(1); })
            .Step("Step2",
                async () => { cts.Cancel(); await Task.Delay(100, cts.Token); return "r2"; },
                async _ => { compensated.Add("Comp2"); await Task.Delay(1); })
            .ExecuteAsync<string>(cts.Token);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsCancelled.Should().BeTrue();
        compensated.Should().Contain("Comp1");
    }

    [Fact]
    public async Task Flow_NoCompensation_ShouldNotFail()
    {
        // Arrange & Act
        var result = await Flow.Create("TestFlow")
            .Step("Step1", async () => { await Task.Delay(1); return "r1"; })
            .Step("Step2", async () => { await Task.Delay(1); throw new Exception("fail"); return "r2"; })
            .ExecuteAsync<string>();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.FailedStep.Should().Be("Step2");
    }

    [Fact]
    public async Task Flow_Duration_ShouldBeTracked()
    {
        // Arrange & Act
        var result = await Flow.Create("TestFlow")
            .Step("Step1", async () => { await Task.Delay(50); return "r1"; })
            .ExecuteAsync<string>();

        // Assert
        result.Duration.TotalMilliseconds.Should().BeGreaterThan(40);
    }
}
