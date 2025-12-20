using Catga.Flow;
using CatgaFlow = Catga.Flow.Flow;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow;

/// <summary>
/// Comprehensive tests for Flow, FlowExecutor, FlowState, FlowResult
/// </summary>
public class FlowComprehensiveTests
{
    #region Flow (Simple In-Memory) Tests

    [Fact]
    public async Task Flow_Create_ShouldCreateNewFlow()
    {
        var flow = CatgaFlow.Create("test-flow");
        flow.Should().NotBeNull();
    }

    [Fact]
    public async Task Flow_SingleStep_ShouldExecuteSuccessfully()
    {
        var executed = false;
        var flow = CatgaFlow.Create("test")
            .Step(() =>
            {
                executed = true;
                return Task.CompletedTask;
            });

        var result = await flow.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(1);
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task Flow_MultipleSteps_ShouldExecuteInOrder()
    {
        var order = new List<int>();
        var flow = CatgaFlow.Create("test")
            .Step(() => { order.Add(1); return Task.CompletedTask; })
            .Step(() => { order.Add(2); return Task.CompletedTask; })
            .Step(() => { order.Add(3); return Task.CompletedTask; });

        var result = await flow.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(3);
        order.Should().BeEquivalentTo([1, 2, 3], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Flow_WithCancellationToken_ShouldPassToken()
    {
        var tokenReceived = false;
        var flow = CatgaFlow.Create("test")
            .Step(ct =>
            {
                tokenReceived = !ct.IsCancellationRequested;
                return Task.CompletedTask;
            });

        var result = await flow.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        tokenReceived.Should().BeTrue();
    }

    [Fact]
    public async Task Flow_OnFailure_ShouldReturnFailureResult()
    {
        var flow = CatgaFlow.Create("test")
            .Step(() => throw new InvalidOperationException("Step failed"));

        var result = await flow.ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Step failed");
        result.CompletedSteps.Should().Be(0);
    }

    [Fact]
    public async Task Flow_OnFailure_ShouldExecuteCompensation()
    {
        var compensated = false;
        var flow = CatgaFlow.Create("test")
            .Step(() => Task.CompletedTask, () => { compensated = true; return Task.CompletedTask; })
            .Step(() => throw new InvalidOperationException("Step 2 failed"));

        var result = await flow.ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        compensated.Should().BeTrue();
    }

    [Fact]
    public async Task Flow_OnFailure_ShouldCompensateInReverseOrder()
    {
        var compensationOrder = new List<int>();
        var flow = CatgaFlow.Create("test")
            .Step(() => Task.CompletedTask, () => { compensationOrder.Add(1); return Task.CompletedTask; })
            .Step(() => Task.CompletedTask, () => { compensationOrder.Add(2); return Task.CompletedTask; })
            .Step(() => throw new InvalidOperationException("Step 3 failed"));

        var result = await flow.ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        compensationOrder.Should().BeEquivalentTo([2, 1], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Flow_OnCancellation_ShouldReturnCancelledResult()
    {
        var cts = new CancellationTokenSource();
        var flow = CatgaFlow.Create("test")
            .Step(ct =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        var result = await flow.ExecuteAsync(cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task Flow_ExecuteFromAsync_ShouldStartFromSpecifiedStep()
    {
        var executedSteps = new List<int>();
        var flow = CatgaFlow.Create("test")
            .Step(() => { executedSteps.Add(1); return Task.CompletedTask; })
            .Step(() => { executedSteps.Add(2); return Task.CompletedTask; })
            .Step(() => { executedSteps.Add(3); return Task.CompletedTask; });

        var result = await flow.ExecuteFromAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(3);
        executedSteps.Should().BeEquivalentTo([2, 3], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Flow_ExecuteFromAsync_OnFailure_ShouldOnlyCompensateExecutedSteps()
    {
        var compensationOrder = new List<int>();
        var flow = CatgaFlow.Create("test")
            .Step(() => Task.CompletedTask, () => { compensationOrder.Add(1); return Task.CompletedTask; })
            .Step(() => Task.CompletedTask, () => { compensationOrder.Add(2); return Task.CompletedTask; })
            .Step(() => throw new InvalidOperationException("Failed"), () => { compensationOrder.Add(3); return Task.CompletedTask; });

        var result = await flow.ExecuteFromAsync(1);

        result.IsSuccess.Should().BeFalse();
        // Only step 2 should be compensated (step 1 was skipped, step 3 failed before completion)
        compensationOrder.Should().BeEquivalentTo([2], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Flow_Duration_ShouldBeTracked()
    {
        var flow = CatgaFlow.Create("test")
            .Step(async () => await Task.Delay(50));

        var result = await flow.ExecuteAsync();

        result.Duration.Should().BeGreaterThan(TimeSpan.FromMilliseconds(40));
    }

    [Fact]
    public async Task Flow_CompensationFailure_ShouldContinueWithOtherCompensations()
    {
        var compensated = new List<int>();
        var flow = CatgaFlow.Create("test")
            .Step(() => Task.CompletedTask, () => { compensated.Add(1); return Task.CompletedTask; })
            .Step(() => Task.CompletedTask, () => throw new InvalidOperationException("Compensation failed"))
            .Step(() => throw new InvalidOperationException("Step failed"));

        var result = await flow.ExecuteAsync();

        result.IsSuccess.Should().BeFalse();
        // First compensation should still run even though second failed
        compensated.Should().Contain(1);
    }

    #endregion

    #region FlowState Tests

    [Fact]
    public void FlowState_ShouldHaveRequiredProperties()
    {
        var state = new FlowState
        {
            Id = "flow-123",
            Type = "OrderFlow",
            Status = FlowStatus.Running,
            Step = 2,
            Version = 1,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = new byte[] { 1, 2, 3 },
            Error = null
        };

        state.Id.Should().Be("flow-123");
        state.Type.Should().Be("OrderFlow");
        state.Status.Should().Be(FlowStatus.Running);
        state.Step.Should().Be(2);
        state.Version.Should().Be(1);
        state.Owner.Should().Be("node-1");
        state.Data.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        state.Error.Should().BeNull();
    }

    [Fact]
    public void FlowStatus_ShouldHaveCorrectValues()
    {
        ((byte)FlowStatus.Running).Should().Be(0);
        ((byte)FlowStatus.Compensating).Should().Be(1);
        ((byte)FlowStatus.Done).Should().Be(2);
        ((byte)FlowStatus.Failed).Should().Be(3);
    }

    #endregion

    #region FlowResult Tests

    [Fact]
    public void FlowResult_Success_ShouldHaveCorrectProperties()
    {
        var result = new FlowResult(true, 5, TimeSpan.FromSeconds(1));

        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(5);
        result.Duration.Should().Be(TimeSpan.FromSeconds(1));
        result.Error.Should().BeNull();
        result.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public void FlowResult_Failure_ShouldHaveCorrectProperties()
    {
        var result = new FlowResult(false, 3, TimeSpan.FromSeconds(2), "Something went wrong");

        result.IsSuccess.Should().BeFalse();
        result.CompletedSteps.Should().Be(3);
        result.Duration.Should().Be(TimeSpan.FromSeconds(2));
        result.Error.Should().Be("Something went wrong");
    }

    [Fact]
    public void FlowResult_WithFlowId_ShouldSetFlowId()
    {
        var result = new FlowResult(true, 1, TimeSpan.Zero) { FlowId = "flow-123" };

        result.FlowId.Should().Be("flow-123");
    }

    [Fact]
    public void FlowResultGeneric_Success_ShouldHaveCorrectProperties()
    {
        var result = new FlowResult<string>(true, "result-value", 5, TimeSpan.FromSeconds(1));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("result-value");
        result.CompletedSteps.Should().Be(5);
        result.Duration.Should().Be(TimeSpan.FromSeconds(1));
        result.Error.Should().BeNull();
    }

    [Fact]
    public void FlowResultGeneric_Failure_ShouldHaveCorrectProperties()
    {
        var result = new FlowResult<string>(false, null, 3, TimeSpan.FromSeconds(2), "Error");

        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("Error");
    }

    #endregion

    #region FlowOptions Tests

    [Fact]
    public void FlowOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new FlowOptions();

        options.NodeId.Should().BeNull();
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.ClaimTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void FlowOptions_CustomValues_ShouldBeSet()
    {
        var options = new FlowOptions
        {
            NodeId = "custom-node",
            HeartbeatInterval = TimeSpan.FromSeconds(10),
            ClaimTimeout = TimeSpan.FromMinutes(1)
        };

        options.NodeId.Should().Be("custom-node");
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(10));
        options.ClaimTimeout.Should().Be(TimeSpan.FromMinutes(1));
    }

    #endregion

    #region FlowExecutor Tests

    [Fact]
    public async Task FlowExecutor_ExecuteAsync_NewFlow_ShouldCreateAndExecute()
    {
        var store = Substitute.For<IFlowStore>();
        store.CreateAsync(Arg.Any<FlowState>(), Arg.Any<CancellationToken>())
            .Returns(true);
        store.UpdateAsync(Arg.Any<FlowState>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var executor = new FlowExecutor(store);
        var executed = false;

        var result = await executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) =>
            {
                executed = true;
                return Task.FromResult(new FlowResult(true, 1, TimeSpan.Zero));
            });

        result.IsSuccess.Should().BeTrue();
        result.FlowId.Should().Be("flow-1");
        executed.Should().BeTrue();
        await store.Received(1).CreateAsync(Arg.Any<FlowState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlowExecutor_ExecuteAsync_ExistingDoneFlow_ShouldReturnSuccess()
    {
        var store = Substitute.For<IFlowStore>();
        store.CreateAsync(Arg.Any<FlowState>(), Arg.Any<CancellationToken>())
            .Returns(false); // Already exists
        store.GetAsync("flow-1", Arg.Any<CancellationToken>())
            .Returns(new FlowState
            {
                Id = "flow-1",
                Type = "TestFlow",
                Status = FlowStatus.Done,
                Step = 5
            });

        var executor = new FlowExecutor(store);

        var result = await executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) => Task.FromResult(new FlowResult(true, 1, TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
        result.FlowId.Should().Be("flow-1");
    }

    [Fact]
    public async Task FlowExecutor_ExecuteAsync_ExistingFailedFlow_ShouldReturnFailure()
    {
        var store = Substitute.For<IFlowStore>();
        store.CreateAsync(Arg.Any<FlowState>(), Arg.Any<CancellationToken>())
            .Returns(false);
        store.GetAsync("flow-1", Arg.Any<CancellationToken>())
            .Returns(new FlowState
            {
                Id = "flow-1",
                Type = "TestFlow",
                Status = FlowStatus.Failed,
                Step = 3,
                Error = "Previous error"
            });

        var executor = new FlowExecutor(store);

        var result = await executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) => Task.FromResult(new FlowResult(true, 1, TimeSpan.Zero)));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Previous error");
    }

    [Fact]
    public async Task FlowExecutor_ExecuteAsync_OnException_ShouldUpdateStateToFailed()
    {
        var store = Substitute.For<IFlowStore>();
        store.CreateAsync(Arg.Any<FlowState>(), Arg.Any<CancellationToken>())
            .Returns(true);
        store.UpdateAsync(Arg.Any<FlowState>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var executor = new FlowExecutor(store);

        var result = await executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) => throw new InvalidOperationException("Executor failed"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Executor failed");
        await store.Received().UpdateAsync(
            Arg.Is<FlowState>(s => s.Status == FlowStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
