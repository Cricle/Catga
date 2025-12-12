using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;
using System.Linq.Expressions;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests for When expression-based conditions and Into() method
/// Tests expression evaluation and state modification
/// </summary>
public class WhenAndIntoTests
{
    [Fact]
    public async Task When_SimpleExpression_ShouldExecuteWhenTrue()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleWhenFlow();
        var executor = new DslFlowExecutor<WhenTestState, SimpleWhenFlow>(mediator, store, config);

        var state = new WhenTestState { FlowId = "when-simple", Value = 100 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task When_ExpressionFalse_ShouldNotExecute()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleWhenFlow();
        var executor = new DslFlowExecutor<WhenTestState, SimpleWhenFlow>(mediator, store, config);

        var state = new WhenTestState { FlowId = "when-false", Value = 50 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Processed.Should().BeFalse();
    }

    [Fact]
    public async Task When_ComplexExpression_ShouldEvaluateCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ComplexWhenFlow();
        var executor = new DslFlowExecutor<WhenTestState, ComplexWhenFlow>(mediator, store, config);

        var state = new WhenTestState { FlowId = "when-complex", Value = 150, Status = "active" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Into_SimpleAction_ShouldModifyState()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new IntoSimpleFlow();
        var executor = new DslFlowExecutor<IntoTestState, IntoSimpleFlow>(mediator, store, config);

        var state = new IntoTestState { FlowId = "into-simple", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(1);
    }

    [Fact]
    public async Task Into_MultipleActions_ShouldExecuteSequentially()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new IntoMultipleFlow();
        var executor = new DslFlowExecutor<IntoTestState, IntoMultipleFlow>(mediator, store, config);

        var state = new IntoTestState { FlowId = "into-multiple", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(3);
        state.Log.Should().HaveCount(3);
    }

    [Fact]
    public async Task Into_WithComplexStateModification_ShouldPersist()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new IntoComplexFlow();
        var executor = new DslFlowExecutor<IntoTestState, IntoComplexFlow>(mediator, store, config);

        var state = new IntoTestState { FlowId = "into-complex", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Data.Should().NotBeEmpty();
        state.Data.Should().ContainKey("timestamp");
    }

    [Fact]
    public async Task Into_InLoop_ShouldModifyStateEachIteration()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new IntoInLoopFlow();
        var executor = new DslFlowExecutor<IntoTestState, IntoInLoopFlow>(mediator, store, config);

        var state = new IntoTestState { FlowId = "into-loop", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(5);
        state.Log.Should().HaveCount(5);
    }

    [Fact]
    public async Task Into_InBranch_ShouldOnlyModifyInExecutedBranch()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new IntoInBranchFlow();
        var executor = new DslFlowExecutor<IntoTestState, IntoInBranchFlow>(mediator, store, config);

        var state = new IntoTestState { FlowId = "into-branch", Counter = 100 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Log.Should().Contain("HighValue");
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(Task.CompletedTask);
    }
}

// Test states
public class WhenTestState : TestStateBase
{
    public int Value { get; set; }
    public string Status { get; set; }
    public bool Processed { get; set; }
}

public class IntoTestState : TestStateBase
{
    public int Counter { get; set; }
    public List<string> Log { get; set; } = new();
    public Dictionary<string, object> Data { get; set; } = new();
}

// When flow configurations
public class SimpleWhenFlow : FlowConfig<WhenTestState>
{
    protected override void Configure(IFlowBuilder<WhenTestState> flow)
    {
        flow
            .When(s => s.Value > 75)
                .Into(s => s.Processed = true)
            .EndWhen();
    }
}

public class ComplexWhenFlow : FlowConfig<WhenTestState>
{
    protected override void Configure(IFlowBuilder<WhenTestState> flow)
    {
        flow
            .When(s => s.Value > 100 && s.Status == "active")
                .Into(s => s.Processed = true)
            .EndWhen();
    }
}

// Into flow configurations
public class IntoSimpleFlow : FlowConfig<IntoTestState>
{
    protected override void Configure(IFlowBuilder<IntoTestState> flow)
    {
        flow
            .Into(s => s.Counter++);
    }
}

public class IntoMultipleFlow : FlowConfig<IntoTestState>
{
    protected override void Configure(IFlowBuilder<IntoTestState> flow)
    {
        flow
            .Into(s => s.Counter++)
            .Into(s => s.Counter++)
            .Into(s => s.Counter++)
            .Into(s => s.Log.Add($"Counter: {s.Counter}"))
            .Into(s => s.Log.Add($"Counter: {s.Counter}"))
            .Into(s => s.Log.Add($"Counter: {s.Counter}"));
    }
}

public class IntoComplexFlow : FlowConfig<IntoTestState>
{
    protected override void Configure(IFlowBuilder<IntoTestState> flow)
    {
        flow
            .Into(s =>
            {
                s.Counter = 10;
                s.Data["timestamp"] = DateTime.UtcNow;
                s.Data["processed"] = true;
                s.Log.Add("Initialized");
            });
    }
}

public class IntoInLoopFlow : FlowConfig<IntoTestState>
{
    protected override void Configure(IFlowBuilder<IntoTestState> flow)
    {
        flow
            .While(s => s.Counter < 5)
                .Into(s => s.Counter++)
                .Into(s => s.Log.Add($"Iteration {s.Counter}"))
            .EndWhile();
    }
}

public class IntoInBranchFlow : FlowConfig<IntoTestState>
{
    protected override void Configure(IFlowBuilder<IntoTestState> flow)
    {
        flow
            .If(s => s.Counter > 50)
                .Into(s => s.Log.Add("HighValue"))
            .Else(f => f
                .Into(s => s.Log.Add("LowValue"))
            )
            .EndIf();
    }
}
