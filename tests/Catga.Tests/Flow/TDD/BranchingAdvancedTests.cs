using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// Advanced TDD tests for If/ElseIf/Else and Switch/Case branching
/// Tests complex branching scenarios and nested conditions
/// </summary>
public class BranchingAdvancedTests
{
    [Fact]
    public async Task If_MultipleElseIf_ShouldExecuteCorrectBranch()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new MultipleElseIfFlow();
        var executor = new DslFlowExecutor<BranchingTestState, MultipleElseIfFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "if-multi-elseif", Value = 50 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedPath.Should().Contain("Medium");
    }

    [Fact]
    public async Task If_NestedConditions_ShouldEvaluateCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new NestedIfFlow();
        var executor = new DslFlowExecutor<BranchingTestState, NestedIfFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "if-nested", Value = 75 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedPath.Should().NotBeEmpty();
    }

    [Fact]
    public async Task If_ElseWithoutCondition_ShouldExecuteWhenOthersFail()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new IfElseDefaultFlow();
        var executor = new DslFlowExecutor<BranchingTestState, IfElseDefaultFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "if-else-default", Value = 999 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedPath.Should().Contain("Default");
    }

    [Fact]
    public async Task Switch_MultipleCase_ShouldMatchCorrectValue()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SwitchMultipleCaseFlow();
        var executor = new DslFlowExecutor<BranchingTestState, SwitchMultipleCaseFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "switch-multi", Status = "pending" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedPath.Should().Contain("Pending");
    }

    [Fact]
    public async Task Switch_DefaultCase_ShouldExecuteWhenNoMatch()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SwitchDefaultFlow();
        var executor = new DslFlowExecutor<BranchingTestState, SwitchDefaultFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "switch-default", Status = "unknown" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutedPath.Should().Contain("Unknown");
    }

    [Fact]
    public async Task If_WithComplexCondition_ShouldEvaluateExpression()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ComplexConditionFlow();
        var executor = new DslFlowExecutor<BranchingTestState, ComplexConditionFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "if-complex", Value = 100, Status = "active" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task If_StateModificationInBranch_ShouldPersist()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new IfStateModificationFlow();
        var executor = new DslFlowExecutor<BranchingTestState, IfStateModificationFlow>(mediator, store, config);

        var state = new BranchingTestState { FlowId = "if-state-mod", Value = 50 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Metadata.Should().NotBeEmpty();
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(Task.CompletedTask);
    }
}

// Test state for branching
public class BranchingTestState : TestStateBase
{
    public int Value { get; set; }
    public string Status { get; set; }
    public List<string> ExecutedPath { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

// Advanced branching configurations
public class MultipleElseIfFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow
            .If(s => s.Value < 25)
                .Into(s => s.ExecutedPath.Add("Low"))
            .ElseIf(s => s.Value < 75)
                .Into(s => s.ExecutedPath.Add("Medium"))
            .ElseIf(s => s.Value < 100)
                .Into(s => s.ExecutedPath.Add("High"))
            .Else(f => f
                .Into(s => s.ExecutedPath.Add("VeryHigh"))
            )
            .EndIf();
    }
}

public class NestedIfFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow
            .If(s => s.Value > 50)
                .If(s => s.Value > 75)
                    .Into(s => s.ExecutedPath.Add("HighValue"))
                .Else(f => f
                    .Into(s => s.ExecutedPath.Add("MediumValue"))
                )
                .EndIf()
            .EndIf();
    }
}

public class IfElseDefaultFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow
            .If(s => s.Value < 100)
                .Into(s => s.ExecutedPath.Add("Low"))
            .ElseIf(s => s.Value < 500)
                .Into(s => s.ExecutedPath.Add("Medium"))
            .Else(f => f
                .Into(s => s.ExecutedPath.Add("Default"))
            )
            .EndIf();
    }
}

public class SwitchMultipleCaseFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow
            .Switch(s => s.Status)
            .Case("pending", c => c
                .Into(s => s.ExecutedPath.Add("Pending"))
            )
            .Case("active", c => c
                .Into(s => s.ExecutedPath.Add("Active"))
            )
            .Case("completed", c => c
                .Into(s => s.ExecutedPath.Add("Completed"))
            )
            .Default(c => c
                .Into(s => s.ExecutedPath.Add("Unknown"))
            )
            .EndSwitch();
    }
}

public class SwitchDefaultFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow
            .Switch(s => s.Status)
            .Case("active", c => c
                .Into(s => s.ExecutedPath.Add("Active"))
            )
            .Case("completed", c => c
                .Into(s => s.ExecutedPath.Add("Completed"))
            )
            .Default(c => c
                .Into(s => s.ExecutedPath.Add("Unknown"))
            )
            .EndSwitch();
    }
}

public class ComplexConditionFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow
            .If(s => s.Value >= 100 && s.Status == "active")
                .Into(s => s.ExecutedPath.Add("HighValueActive"))
            .Else(f => f
                .Into(s => s.ExecutedPath.Add("Other"))
            )
            .EndIf();
    }
}

public class IfStateModificationFlow : FlowConfig<BranchingTestState>
{
    protected override void Configure(IFlowBuilder<BranchingTestState> flow)
    {
        flow
            .If(s => s.Value > 25)
                .Into(s => s.Metadata["processed"] = true)
                .Into(s => s.Metadata["timestamp"] = DateTime.UtcNow)
            .EndIf();
    }
}
