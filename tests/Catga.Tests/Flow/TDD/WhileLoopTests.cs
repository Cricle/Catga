using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests for While/DoWhile/Repeat loop functionality
/// Tests control flow, safety limits, storage parity, and recovery
/// </summary>
public class WhileLoopTests
{
    [Fact]
    public async Task While_SimpleCondition_ShouldExecuteUntilConditionFalse()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleWhileFlow();
        var executor = new DslFlowExecutor<WhileTestState, SimpleWhileFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-simple", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(5, "loop should execute 5 times (0,1,2,3,4)");
        state.ExecutionLog.Should().HaveCount(5);
    }

    [Fact]
    public async Task While_DepthLimit_ShouldThrowWhenExceeded()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new InfiniteWhileFlow();
        var executor = new DslFlowExecutor<WhileTestState, InfiniteWhileFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-depth-limit", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse("infinite loop should fail");
        result.Error.Should().Contain("depth limit", "should mention depth limit");
    }

    [Fact]
    public async Task While_IterationLimit_ShouldThrowWhenExceeded()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new LargeIterationWhileFlow();
        var executor = new DslFlowExecutor<WhileTestState, LargeIterationWhileFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-iteration-limit", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse("too many iterations should fail");
        result.Error.Should().Contain("iteration", "should mention iteration limit");
    }

    [Fact]
    public async Task While_Timeout_ShouldThrowWhenExceeded()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SlowWhileFlow();
        var executor = new DslFlowExecutor<WhileTestState, SlowWhileFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-timeout", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse("timeout should fail");
        result.Error.Should().Contain("timeout", "should mention timeout");
    }

    [Fact]
    public async Task DoWhile_ExecutesAtLeastOnce()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleDoWhileFlow();
        var executor = new DslFlowExecutor<WhileTestState, SimpleDoWhileFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "dowhile-simple", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ExecutionLog.Should().NotBeEmpty("do-while should execute at least once");
    }

    [Fact]
    public async Task Repeat_ExecutesExactTimes()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleRepeatFlow();
        var executor = new DslFlowExecutor<WhileTestState, SimpleRepeatFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "repeat-simple", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(10, "repeat should execute exactly 10 times");
        state.ExecutionLog.Should().HaveCount(10);
    }

    [Fact]
    public async Task While_NestedLoops_ShouldWork()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new NestedWhileFlow();
        var executor = new DslFlowExecutor<WhileTestState, NestedWhileFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-nested", Counter = 0 };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(25, "nested loops should execute 5*5 times");
    }

    [Fact]
    public async Task While_InMemoryStorage_ShouldRecover()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleWhileFlow();
        var executor = new DslFlowExecutor<WhileTestState, SimpleWhileFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-recovery-memory", Counter = 0 };
        SetupMediator(mediator);

        // Act - First execution
        var result1 = await executor.RunAsync(state);

        // Simulate crash and recovery
        var state2 = await store.GetAsync<WhileTestState>("while-recovery-memory");
        var executor2 = new DslFlowExecutor<WhileTestState, SimpleWhileFlow>(mediator, store, config);
        var result2 = await executor2.RunAsync(state2);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        state2.Counter.Should().Be(5);
    }


    [Fact]
    public async Task While_LoopCounterPersisted_ShouldResumeCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleWhileFlow();
        var executor = new DslFlowExecutor<WhileTestState, SimpleWhileFlow>(mediator, store, config);

        var state = new WhileTestState { FlowId = "while-counter-persist", Counter = 0 };
        SetupMediator(mediator);

        // Act - Simulate partial execution and recovery
        state.Counter = 2; // Simulate that loop executed 2 times
        await store.SaveAsync(state);

        var state2 = await store.GetAsync<WhileTestState>("while-counter-persist");
        var executor2 = new DslFlowExecutor<WhileTestState, SimpleWhileFlow>(mediator, store, config);
        var result = await executor2.RunAsync(state2);

        // Assert
        result.IsSuccess.Should().BeTrue();
        state2.Counter.Should().Be(5, "should resume from counter 2 and reach 5");
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(x => Task.FromResult<IResponse>(new SuccessResponse()));
    }
}

// Test state class
public class WhileTestState : TestStateBase
{
    public int Counter { get; set; }
    public List<string> ExecutionLog { get; set; } = new();
    public Dictionary<string, object> Variables { get; set; } = new();
}

// Test flow configurations
public class SimpleWhileFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .While(s => s.Counter < 5)
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutionLog.Add($"Iteration {s.Counter}"))
                .Into(s => s.Counter++)
            .EndWhile();
    }
}

public class InfiniteWhileFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .While(s => true) // Infinite loop
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
            .EndWhile();
    }
}

public class LargeIterationWhileFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .While(s => s.Counter < 20000) // More than iteration limit
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
                .Into(s => s.Counter++)
            .EndWhile();
    }
}

public class SlowWhileFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .While(s => s.Counter < 100)
                .Send(s => new SlowCommand { FlowId = s.FlowId, DelayMs = 1000 })
                .Into(s => s.Counter++)
            .EndWhile();
    }
}

public class SimpleDoWhileFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .DoWhile()
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutionLog.Add($"Iteration {s.Counter}"))
                .Into(s => s.Counter++)
            .Until(s => s.Counter >= 5);
    }
}

public class SimpleRepeatFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .Repeat(10)
                .Send(s => new IncrementCommand { FlowId = s.FlowId })
                .Into(s => s.ExecutionLog.Add($"Iteration {s.Counter}"))
                .Into(s => s.Counter++)
            .EndRepeat();
    }
}

public class NestedWhileFlow : FlowConfig<WhileTestState>
{
    protected override void Configure(IFlowBuilder<WhileTestState> flow)
    {
        flow
            .While(s => s.Counter < 25)
                .While(s => s.Counter % 5 < 5)
                    .Send(s => new IncrementCommand { FlowId = s.FlowId })
                    .Into(s => s.Counter++)
                .EndWhile()
            .EndWhile();
    }
}

// Test commands
public class IncrementCommand : IRequest, IMessage
{
    public string FlowId { get; set; }
    public long MessageId { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public class SlowCommand : IRequest, IMessage
{
    public string FlowId { get; set; }
    public int DelayMs { get; set; }
    public long MessageId { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
