using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests for branching logic (If/ElseIf/Else) edge cases and complex scenarios
/// </summary>
public class BranchingLogicTests
{
    [Fact]
    public async Task ExecuteIf_NullCondition_ShouldReturnFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NullConditionFlow();
        var executor = new DslFlowExecutor<BranchingTestState, NullConditionFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "null-condition", Value = 5 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse("null condition should cause failure");
        result.Error.Should().Contain("condition");
    }

    [Fact]
    public async Task ExecuteIf_EmptyBranches_ShouldSucceed()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new EmptyBranchFlow();
        var executor = new DslFlowExecutor<BranchingTestState, EmptyBranchFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "empty-branches", Value = 5 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue("empty branches should be allowed");
        state.ExecutedBranches.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, "Then")]
    [InlineData(1, "ElseIf1")]
    [InlineData(2, "ElseIf2")]
    [InlineData(3, "ElseIf3")]
    [InlineData(4, "ElseIf4")]
    [InlineData(5, "ElseIf5")]
    [InlineData(100, "Else")]
    public async Task ExecuteIf_ManyElseIfBranches_ShouldSelectCorrect(int value, string expectedBranch)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ManyElseIfFlow();
        var executor = new DslFlowExecutor<BranchingTestState, ManyElseIfFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "many-elseif", Value = value };

        SetupMediatorForBranching(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedBranches.Should().Contain(expectedBranch);
        state.ExecutedBranches.Should().HaveCount(1, "only one branch should execute");
    }

    [Fact]
    public async Task ExecuteIf_NestedConditions_ShouldEvaluateCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NestedConditionFlow();
        var executor = new DslFlowExecutor<BranchingTestState, NestedConditionFlow>(mediator, store, config);

        var state = new BranchingTestState
        {
            FlowId = "nested-conditions",
            Value = 15,
            SecondaryValue = 25
        };

        SetupMediatorForBranching(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedBranches.Should().BeEquivalentTo(["Outer-Then", "Inner-ElseIf", "Nested-Then"]);
    }

    [Fact]
    public async Task ExecuteIf_ConditionThrowsException_ShouldHandleGracefully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new ExceptionConditionFlow();
        var executor = new DslFlowExecutor<BranchingTestState, ExceptionConditionFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "exception-condition", Value = -1 };

        // Act
        var act = () => executor.RunAsync(state);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteIf_StateModificationInCondition_ShouldNotAffectBranching()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new StateModifyingConditionFlow();
        var executor = new DslFlowExecutor<BranchingTestState, StateModifyingConditionFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "state-modifying", Value = 5 };

        SetupMediatorForBranching(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Value.Should().Be(10, "state should be modified");
        state.ExecutedBranches.Should().Contain("Then", "original condition was true");
    }

    [Fact]
    public async Task ExecuteIf_AllConditionsFalse_NoElse_ShouldSucceed()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new NoMatchingBranchFlow();
        var executor = new DslFlowExecutor<BranchingTestState, NoMatchingBranchFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "no-match", Value = 999 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue("no matching branch is valid");
        state.ExecutedBranches.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(10, 1000)]
    [InlineData(100, 10000)]
    public async Task ExecuteIf_Performance_ManyBranchEvaluations(int branchCount, int iterations)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new PerformanceBranchFlow(branchCount);
        var executor = new DslFlowExecutor<BranchingTestState, PerformanceBranchFlow>(mediator, store, config);

        SetupMediatorForBranching(mediator);

        // Act & Assert
        var tasks = new List<Task>();
        for (int i = 0; i < iterations; i++)
        {
            var state = new BranchingTestState
            {
                FlowId = $"perf-{i}",
                Value = i % branchCount
            };

            tasks.Add(Task.Run(async () =>
            {
                var result = await executor.RunAsync(state);
                result.IsSuccess.Should().BeTrue();
            }));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(tasks);
        sw.Stop();

        // Performance assertion
        var avgTime = sw.Elapsed.TotalMilliseconds / iterations;
        avgTime.Should().BeLessThan(10, $"average time per branch evaluation should be fast");
    }

    private static void SetupMediatorForBranching(ICatgaMediator mediator)
    {
        mediator.SendAsync<BranchCommand, string>(
            Arg.Any<BranchCommand>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<BranchCommand>();
                // Track execution in state via callback
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"executed-{cmd.Branch}"));
            });
    }
}

// Test State
public class BranchingTestState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public int SecondaryValue { get; set; }
    public List<string> ExecutedBranches { get; set; } = [];

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Test Command
public record BranchCommand(string Branch) : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

// Test Flow Configurations
public class NullConditionFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow.Name("null-condition-flow");
        // Intentionally create an If without proper condition setup
        var step = new FlowStep { Type = StepType.If, BranchCondition = null };
        flow.AddStep(step);
    }
}

public class EmptyBranchFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow.Name("empty-branch-flow");

        flow.If(s => s.Value == 5)
            // Empty Then branch
            .ElseIf(s => s.Value == 10)
            // Empty ElseIf branch
            .Else()
            // Empty Else branch
            .EndIf();
    }
}

public class ManyElseIfFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow.Name("many-elseif-flow");

        var ifBuilder = flow.If(s => s.Value == 0);
        ifBuilder.Send(s => new BranchCommand("Then"));

        for (int i = 1; i <= 5; i++)
        {
            var index = i;
            ifBuilder.ElseIf(s => s.Value == index)
                .Send(s => new BranchCommand($"ElseIf{index}"));
        }

        ifBuilder.Else()
            .Send(s => new BranchCommand("Else"));

        ifBuilder.EndIf();
    }
}

public class NestedConditionFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow.Name("nested-condition-flow");

        flow.If(s => s.Value > 10)
            .Send(s => new BranchCommand("Outer-Then"))
            .If(s => s.SecondaryValue > 30)
                .Send(s => new BranchCommand("Inner-Then"))
            .ElseIf(s => s.SecondaryValue > 20)
                .Send(s => new BranchCommand("Inner-ElseIf"))
                .If(s => s.Value < 20)
                    .Send(s => new BranchCommand("Nested-Then"))
                .EndIf()
            .EndIf()
        .EndIf();
    }
}

public class ExceptionConditionFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow.Name("exception-condition-flow");

        flow.If(s => throw new InvalidOperationException("Condition evaluation failed"))
            .Send(s => new BranchCommand("Never"))
        .EndIf();
    }
}

public class StateModifyingConditionFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow.Name("state-modifying-flow");

        flow.If(s =>
        {
            var original = s.Value;
            s.Value = 10; // Modify state during condition evaluation
            return original == 5;
        })
            .Send(s => new BranchCommand("Then"))
        .EndIf();
    }
}

public class NoMatchingBranchFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow.Name("no-matching-branch-flow");

        flow.If(s => s.Value == 1)
            .Send(s => new BranchCommand("Then"))
        .ElseIf(s => s.Value == 2)
            .Send(s => new BranchCommand("ElseIf"))
        // No Else branch
        .EndIf();
    }
}

public class PerformanceBranchFlow : FlowConfig<BranchingTestState>
{
    private readonly int _branchCount;

    public PerformanceBranchFlow(int branchCount)
    {
        _branchCount = branchCount;
    }

    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow.Name("performance-branch-flow");

        var ifBuilder = flow.If(s => s.Value == 0);
        ifBuilder.Send(s => new BranchCommand("Branch0"));

        for (int i = 1; i < _branchCount; i++)
        {
            var index = i;
            ifBuilder.ElseIf(s => s.Value == index)
                .Send(s => new BranchCommand($"Branch{index}"));
        }

        ifBuilder.EndIf();
    }
}
