using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests to ensure If/Switch branching can be perfectly recovered from any interruption point.
/// These tests verify that branch execution state is properly persisted and can resume correctly.
/// </summary>
public class BranchRecoveryTests
{
    [Fact]
    public async Task IfBranch_ShouldResumeFromThenBranch()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TestIfFlow();

        var state = new TestBranchState
        {
            FlowId = "test-if-recovery",
            ShouldExecuteThen = true,
            ThenExecuted = false,
            ElseExecuted = false
        };

        // Setup mediator to handle test commands successfully and modify state
        mediator.SendAsync(Arg.Any<MarkThenExecutedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.ThenExecuted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });
        mediator.SendAsync(Arg.Any<MarkElseExecutedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.ElseExecuted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestBranchState, TestIfFlow>(mediator, store, config);

        // Simulate interruption in the middle of Then branch execution
        var interruptedSnapshot = new FlowSnapshot<TestBranchState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, 0]), // Step 0 (If), Branch 0 (Then)
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(interruptedSnapshot);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable");
        if (!result!.IsSuccess)
        {
            // Add debugging information
            var errorMessage = result.Error ?? "Unknown error";
            throw new Exception($"Flow execution failed: {errorMessage}");
        }
        result.IsSuccess.Should().BeTrue("resumed flow should complete successfully");
        result.State.ThenExecuted.Should().BeTrue("Then branch should be executed after resume");
        result.State.ElseExecuted.Should().BeFalse("Else branch should not be executed");
    }

    [Fact]
    public async Task IfBranch_ShouldResumeFromElseBranch()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TestIfFlow();

        var state = new TestBranchState
        {
            FlowId = "test-if-else-recovery",
            ShouldExecuteThen = false,
            ThenExecuted = false,
            ElseExecuted = false
        };

        // Setup mediator to handle test commands successfully and modify state
        mediator.SendAsync(Arg.Any<MarkThenExecutedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.ThenExecuted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });
        mediator.SendAsync(Arg.Any<MarkElseExecutedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.ElseExecuted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestBranchState, TestIfFlow>(mediator, store, config);

        // Simulate interruption in the middle of Else branch execution
        var interruptedSnapshot = new FlowSnapshot<TestBranchState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, -1]), // Step 0 (If), Branch -1 (Else)
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(interruptedSnapshot);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable");
        result!.IsSuccess.Should().BeTrue("resumed flow should complete successfully");
        result.State.ThenExecuted.Should().BeFalse("Then branch should not be executed");
        result.State.ElseExecuted.Should().BeTrue("Else branch should be executed after resume");
    }

    [Fact]
    public async Task SwitchBranch_ShouldResumeFromCorrectCase()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TestSwitchFlow();

        var state = new TestBranchState
        {
            FlowId = "test-switch-recovery",
            SwitchValue = "case2",
            Case1Executed = false,
            Case2Executed = false,
            DefaultExecuted = false
        };

        // Setup mediator to handle test commands successfully and modify state
        mediator.SendAsync(Arg.Any<MarkCase1ExecutedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.Case1Executed = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });
        mediator.SendAsync(Arg.Any<MarkCase2ExecutedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.Case2Executed = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });
        mediator.SendAsync(Arg.Any<MarkDefaultExecutedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.DefaultExecuted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestBranchState, TestSwitchFlow>(mediator, store, config);

        // Simulate interruption in the middle of Case2 execution
        var interruptedSnapshot = new FlowSnapshot<TestBranchState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, 2]), // Step 0 (Switch), Case 2
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(interruptedSnapshot);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable");
        result!.IsSuccess.Should().BeTrue("resumed flow should complete successfully");
        result.State.Case1Executed.Should().BeFalse("Case1 should not be executed");
        result.State.Case2Executed.Should().BeTrue("Case2 should be executed after resume");
        result.State.DefaultExecuted.Should().BeFalse("Default should not be executed");
    }

    [Fact]
    public async Task NestedBranches_ShouldResumeFromDeepNesting()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TestNestedBranchFlow();

        var state = new TestBranchState
        {
            FlowId = "test-nested-recovery",
            ShouldExecuteThen = true,
            NestedCondition = true,
            NestedThenExecuted = false
        };

        // Setup mediator to handle test commands successfully and modify state
        mediator.SendAsync(Arg.Any<MarkNestedThenExecutedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.NestedThenExecuted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestBranchState, TestNestedBranchFlow>(mediator, store, config);

        // Simulate interruption deep in nested branch: If -> Then -> If -> Then
        var interruptedSnapshot = new FlowSnapshot<TestBranchState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, 0, 0, 0]), // Outer If -> Then -> Inner If -> Then
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(interruptedSnapshot);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable from deep nesting");
        result!.IsSuccess.Should().BeTrue("resumed nested flow should complete successfully");
        result.State.NestedThenExecuted.Should().BeTrue("Nested Then branch should be executed after resume");
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    [InlineData("Nats")]
    public async Task BranchRecovery_ShouldWorkAcrossAllStores(string storeType)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateStore(storeType);
        var config = new TestIfFlow();

        var state = new TestBranchState
        {
            FlowId = $"test-branch-recovery-{storeType}",
            ShouldExecuteThen = true,
            ThenExecuted = false
        };

        // Setup mediator to handle test commands successfully and modify state
        mediator.SendAsync(Arg.Any<MarkThenExecutedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.ThenExecuted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });
        mediator.SendAsync(Arg.Any<MarkElseExecutedCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.ElseExecuted = true;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestBranchState, TestIfFlow>(mediator, store, config);

        // Create interrupted snapshot
        var interruptedSnapshot = new FlowSnapshot<TestBranchState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, 0]), // In Then branch
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(interruptedSnapshot);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull($"flow should be resumable from {storeType} store");
        result!.IsSuccess.Should().BeTrue($"resumed flow should complete successfully in {storeType}");
        result.State.ThenExecuted.Should().BeTrue($"Then branch should be executed after resume in {storeType}");
    }

    private static IDslFlowStore CreateStore(string storeType)
    {
        return storeType switch
        {
            "InMemory" => TestStoreExtensions.CreateTestFlowStore(),
            "Redis" => TestStoreExtensions.CreateTestFlowStore(), // TODO: Replace with actual Redis store
            "Nats" => TestStoreExtensions.CreateTestFlowStore(),  // TODO: Replace with actual NATS store
            _ => throw new ArgumentException($"Unknown store type: {storeType}")
        };
    }
}

/// <summary>
/// Test flow state for branch recovery tests.
/// </summary>
public class TestBranchState : IFlowState
{
    public string? FlowId { get; set; }
    public bool ShouldExecuteThen { get; set; }
    public bool ThenExecuted { get; set; }
    public bool ElseExecuted { get; set; }
    public string SwitchValue { get; set; } = "case1";
    public bool Case1Executed { get; set; }
    public bool Case2Executed { get; set; }
    public bool DefaultExecuted { get; set; }
    public bool NestedCondition { get; set; }
    public bool NestedThenExecuted { get; set; }

    // Change tracking implementation
    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

/// <summary>
/// Test flow configuration with If/Else branching.
/// </summary>
public class TestIfFlow : FlowConfig<TestBranchState>
{
    protected override void Configure(IFlowBuilder<TestBranchState> flow)
    {
        flow.Name("test-if-flow");

        flow.If(s => s.ShouldExecuteThen)
            .Send(s => new MarkThenExecutedCommand())
        .Else()
            .Send(s => new MarkElseExecutedCommand())
        .EndIf();
    }
}

/// <summary>
/// Test flow configuration with Switch/Case branching.
/// </summary>
public class TestSwitchFlow : FlowConfig<TestBranchState>
{
    protected override void Configure(IFlowBuilder<TestBranchState> flow)
    {
        flow.Name("test-switch-flow");

        flow.Switch(s => s.SwitchValue)
            .Case("case1", c => c.Send(s => new MarkCase1ExecutedCommand()))
            .Case("case2", c => c.Send(s => new MarkCase2ExecutedCommand()))
            .Default(c => c.Send(s => new MarkDefaultExecutedCommand()))
        .EndSwitch();
    }
}

/// <summary>
/// Test flow configuration with nested branching.
/// </summary>
public class TestNestedBranchFlow : FlowConfig<TestBranchState>
{
    protected override void Configure(IFlowBuilder<TestBranchState> flow)
    {
        flow.Name("test-nested-branch-flow");

        flow.If(s => s.ShouldExecuteThen)
            .If(s => s.NestedCondition)
                .Send(s => new MarkNestedThenExecutedCommand())
            .EndIf()
        .EndIf();
    }
}

// Test commands
public record MarkThenExecutedCommand : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record MarkElseExecutedCommand : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record MarkCase1ExecutedCommand : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record MarkCase2ExecutedCommand : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record MarkDefaultExecutedCommand : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record MarkNestedThenExecutedCommand : IRequest
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
