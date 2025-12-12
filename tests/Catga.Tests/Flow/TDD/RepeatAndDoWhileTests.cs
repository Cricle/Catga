using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests for Repeat and DoWhile loop functionality
/// Tests fixed iteration and post-condition loops
/// </summary>
public class RepeatAndDoWhileTests
{
    [Fact]
    public async Task Repeat_FixedTimes_ShouldExecuteExactly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new RepeatFixedFlow();
        var executor = new DslFlowExecutor<RepeatTestState, RepeatFixedFlow>(mediator, store, config);

        var state = new RepeatTestState { FlowId = "repeat-fixed", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(5);
        state.ExecutionLog.Should().HaveCount(5);
    }

    [Fact]
    public async Task Repeat_WithBreak_ShouldExitEarly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new RepeatWithBreakFlow();
        var executor = new DslFlowExecutor<RepeatTestState, RepeatWithBreakFlow>(mediator, store, config);

        var state = new RepeatTestState { FlowId = "repeat-break", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().BeLessThan(10);
    }

    [Fact]
    public async Task Repeat_DynamicTimes_ShouldUseSelector()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new RepeatDynamicFlow();
        var executor = new DslFlowExecutor<RepeatTestState, RepeatDynamicFlow>(mediator, store, config);

        var state = new RepeatTestState { FlowId = "repeat-dynamic", Counter = 0, RepeatCount = 7 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(7);
    }

    [Fact]
    public async Task DoWhile_ExecutesAtLeastOnce()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new DoWhileAtLeastOnceFlow();
        var executor = new DslFlowExecutor<RepeatTestState, DoWhileAtLeastOnceFlow>(mediator, store, config);

        var state = new RepeatTestState { FlowId = "dowhile-once", Counter = 100 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutionLog.Should().NotBeEmpty("do-while should execute at least once");
    }

    [Fact]
    public async Task DoWhile_PostCondition_ShouldCheckAfterExecution()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new DoWhilePostConditionFlow();
        var executor = new DslFlowExecutor<RepeatTestState, DoWhilePostConditionFlow>(mediator, store, config);

        var state = new RepeatTestState { FlowId = "dowhile-post", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DoWhile_WithStateModification_ShouldPersist()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new DoWhileStateModificationFlow();
        var executor = new DslFlowExecutor<RepeatTestState, DoWhileStateModificationFlow>(mediator, store, config);

        var state = new RepeatTestState { FlowId = "dowhile-state", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Variables.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Repeat_WithMultipleSends_ShouldExecuteAll()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new RepeatMultipleSendsFlow();
        var executor = new DslFlowExecutor<RepeatTestState, RepeatMultipleSendsFlow>(mediator, store, config);

        var state = new RepeatTestState { FlowId = "repeat-multi-send", Counter = 0 };
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

// Test state for repeat/dowhile
public class RepeatTestState : TestStateBase
{
    public int Counter { get; set; }
    public int RepeatCount { get; set; }
    public List<string> ExecutionLog { get; set; } = new();
    public Dictionary<string, object> Variables { get; set; } = new();
}

// Repeat flow configurations
public class RepeatFixedFlow : FlowConfig<RepeatTestState>
{
    protected override void Configure(IFlowBuilder<RepeatTestState> flow)
    {
        flow
            .Repeat(5)
                .Send(s => new RepeatCommand { FlowId = s.FlowId })
                .Into(s => s.Counter++)
                .Into(s => s.ExecutionLog.Add($"Iteration {s.Counter}"))
            .EndRepeat();
    }
}

public class RepeatWithBreakFlow : FlowConfig<RepeatTestState>
{
    protected override void Configure(IFlowBuilder<RepeatTestState> flow)
    {
        flow
            .Repeat(10)
                .Send(s => new RepeatCommand { FlowId = s.FlowId })
                .Into(s => s.Counter++)
                .BreakIf(s => s.Counter >= 5)
            .EndRepeat();
    }
}

public class RepeatDynamicFlow : FlowConfig<RepeatTestState>
{
    protected override void Configure(IFlowBuilder<RepeatTestState> flow)
    {
        flow
            .Repeat(s => s.RepeatCount)
                .Send(s => new RepeatCommand { FlowId = s.FlowId })
                .Into(s => s.Counter++)
            .EndRepeat();
    }
}

// DoWhile flow configurations
public class DoWhileAtLeastOnceFlow : FlowConfig<RepeatTestState>
{
    protected override void Configure(IFlowBuilder<RepeatTestState> flow)
    {
        flow
            .DoWhile()
                .Send(s => new RepeatCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutionLog.Add($"Iteration {s.Counter}"))
                .Into(s => s.Counter++)
            .Until(s => s.Counter >= 5);
    }
}

public class DoWhilePostConditionFlow : FlowConfig<RepeatTestState>
{
    protected override void Configure(IFlowBuilder<RepeatTestState> flow)
    {
        flow
            .DoWhile()
                .Send(s => new RepeatCommand { FlowId = s.FlowId })
                .Into(s => s.Counter++)
            .Until(s => s.Counter >= 3);
    }
}

public class DoWhileStateModificationFlow : FlowConfig<RepeatTestState>
{
    protected override void Configure(IFlowBuilder<RepeatTestState> flow)
    {
        flow
            .Into(s => s.Variables["iterations"] = 0)
            .DoWhile()
                .Send(s => new RepeatCommand { FlowId = s.FlowId })
                .Into(s => s.Counter++)
                .Into(s => s.Variables["iterations"] = (int)s.Variables["iterations"] + 1)
            .Until(s => s.Counter >= 3);
    }
}

public class RepeatMultipleSendsFlow : FlowConfig<RepeatTestState>
{
    protected override void Configure(IFlowBuilder<RepeatTestState> flow)
    {
        flow
            .Repeat(3)
                .Send(s => new RepeatCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutionLog.Add($"First send: {s.Counter}"))
                .Send(s => new RepeatCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutionLog.Add($"Second send: {s.Counter}"))
                .Into(s => s.Counter++)
            .EndRepeat();
    }
}

// Test command
public class RepeatCommand : IRequest, IMessage
{
    public string FlowId { get; set; }
    public long MessageId { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
