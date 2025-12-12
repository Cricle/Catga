using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// Advanced TDD tests for While loop functionality
/// Tests complex scenarios, edge cases, and advanced features
/// </summary>
public class WhileLoopAdvancedTests
{
    [Fact]
    public async Task While_WithBreakCondition_ShouldExitEarly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new WhileWithBreakFlow();
        var executor = new DslFlowExecutor<WhileTestState, WhileWithBreakFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-break", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().BeLessThan(10, "loop should break early");
    }

    [Fact]
    public async Task While_WithContinueCondition_ShouldSkipSteps()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new WhileWithContinueFlow();
        var executor = new DslFlowExecutor<WhileTestState, WhileWithContinueFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-continue", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutionLog.Should().NotBeEmpty();
    }

    [Fact]
    public async Task While_WithComplexCondition_ShouldEvaluateCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new WhileComplexConditionFlow();
        var executor = new DslFlowExecutor<WhileTestState, WhileComplexConditionFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-complex", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(10);
    }

    [Fact]
    public async Task While_WithStateModification_ShouldPersistChanges()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new WhileStateModificationFlow();
        var executor = new DslFlowExecutor<WhileTestState, WhileStateModificationFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-state-mod", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Variables.Should().NotBeEmpty();
        state.Variables.Should().ContainKey("iterations");
    }

    [Fact]
    public async Task While_EmptyLoop_ShouldCompleteSuccessfully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new WhileEmptyConditionFlow();
        var executor = new DslFlowExecutor<WhileTestState, WhileEmptyConditionFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-empty", Counter = 10 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(10, "loop should not execute");
    }

    [Fact]
    public async Task While_WithMultipleSends_ShouldExecuteAll()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new WhileMultipleSendsFlow();
        var executor = new DslFlowExecutor<WhileTestState, WhileMultipleSendsFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-multi-send", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutionLog.Count.Should().BeGreaterThan(0);
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(Task.CompletedTask);
    }
}

// Advanced While loop configurations
public class WhileWithBreakFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .While(s => s.Counter < 10)
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
                .Into(s => s.Counter++)
                .BreakIf(s => s.Counter >= 5)
            .EndWhile();
    }
}

public class WhileWithContinueFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .While(s => s.Counter < 5)
                .Into(s => s.Counter++)
                .ContinueIf(s => s.Counter % 2 == 0)
                .Into(s => s.ExecutionLog.Add($"Odd iteration: {s.Counter}"))
            .EndWhile();
    }
}

public class WhileComplexConditionFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .While(s => s.Counter < 10 && s.ExecutionLog.Count < 20)
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
                .Into(s => s.Counter++)
                .Into(s => s.ExecutionLog.Add($"Step {s.Counter}"))
            .EndWhile();
    }
}

public class WhileStateModificationFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .Into(s => s.Variables["iterations"] = 0)
            .While(s => s.Counter < 5)
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
                .Into(s => s.Counter++)
                .Into(s => s.Variables["iterations"] = (int)s.Variables["iterations"] + 1)
            .EndWhile();
    }
}

public class WhileEmptyConditionFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .While(s => s.Counter < 5)
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
            .EndWhile();
    }
}

public class WhileMultipleSendsFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .While(s => s.Counter < 3)
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutionLog.Add($"First send: {s.Counter}"))
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutionLog.Add($"Second send: {s.Counter}"))
                .Into(s => s.Counter++)
            .EndWhile();
    }
}
