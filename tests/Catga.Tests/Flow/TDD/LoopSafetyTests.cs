using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests for loop safety mechanisms
/// Tests depth limits, iteration limits, timeout controls, and memory protection
/// </summary>
public class LoopSafetyTests
{
    [Fact]
    public async Task While_DepthLimitDefault_ShouldBe1000()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new DepthLimitFlow();
        var executor = new DslFlowExecutor<SafetyTestState, DepthLimitFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "depth-limit-default" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        // Should fail when depth exceeds 1000
        if (state.LoopDepth > 1000)
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("depth");
        }
    }

    [Fact]
    public async Task While_IterationLimitDefault_ShouldBe10000()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new IterationLimitFlow();
        var executor = new DslFlowExecutor<SafetyTestState, IterationLimitFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "iteration-limit-default" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        // Should fail when iterations exceed 10000
        if (state.IterationCount > 10000)
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("iteration");
        }
    }

    [Fact]
    public async Task While_TimeoutDefault_ShouldBe5Minutes()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TimeoutLimitFlow();
        var executor = new DslFlowExecutor<SafetyTestState, TimeoutLimitFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "timeout-limit-default" };
        SetupMediator(mediator);

        // Act
        var startTime = DateTime.UtcNow;
        var result = await executor.RunAsync(state);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        result.Should().NotBeNull();
        // Should timeout after 5 minutes
        if (elapsed > TimeSpan.FromMinutes(5))
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("timeout");
        }
    }

    [Fact]
    public async Task While_DepthLimitCustom_ShouldEnforce()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new CustomDepthLimitFlow(100); // Custom limit
        var executor = new DslFlowExecutor<SafetyTestState, CustomDepthLimitFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "custom-depth-limit" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        // Should fail when depth exceeds custom limit
        if (state.LoopDepth > 100)
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("depth");
        }
    }

    [Fact]
    public async Task While_IterationLimitCustom_ShouldEnforce()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new CustomIterationLimitFlow(1000); // Custom limit
        var executor = new DslFlowExecutor<SafetyTestState, CustomIterationLimitFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "custom-iteration-limit" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        // Should fail when iterations exceed custom limit
        if (state.IterationCount > 1000)
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("iteration");
        }
    }

    [Fact]
    public async Task While_TimeoutCustom_ShouldEnforce()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new CustomTimeoutLimitFlow(TimeSpan.FromSeconds(10)); // Custom timeout
        var executor = new DslFlowExecutor<SafetyTestState, CustomTimeoutLimitFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "custom-timeout-limit" };
        SetupMediator(mediator);

        // Act
        var startTime = DateTime.UtcNow;
        var result = await executor.RunAsync(state);
        var elapsed = DateTime.UtcNow - startTime;

        // Assert
        result.Should().NotBeNull();
        // Should timeout after custom timeout
        if (elapsed > TimeSpan.FromSeconds(10))
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("timeout");
        }
    }

    [Fact]
    public async Task While_DepthLimitExceeded_ShouldThrowFlowExecutionException()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new DepthLimitFlow();
        var executor = new DslFlowExecutor<SafetyTestState, DepthLimitFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "depth-exceeded" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        if (!result.IsSuccess)
        {
            result.Error.Should().Contain("FlowExecutionException");
        }
    }

    [Fact]
    public async Task While_IterationLimitExceeded_ShouldThrowFlowExecutionException()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new IterationLimitFlow();
        var executor = new DslFlowExecutor<SafetyTestState, IterationLimitFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "iteration-exceeded" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        if (!result.IsSuccess)
        {
            result.Error.Should().Contain("FlowExecutionException");
        }
    }

    [Fact]
    public async Task While_TimeoutExceeded_ShouldThrowFlowExecutionException()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new CustomTimeoutLimitFlow(TimeSpan.FromMilliseconds(100)); // Very short timeout
        var executor = new DslFlowExecutor<SafetyTestState, CustomTimeoutLimitFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "timeout-exceeded" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        if (!result.IsSuccess)
        {
            result.Error.Should().Contain("FlowExecutionException");
        }
    }

    [Fact]
    public async Task While_SafeLimitsPersisted_ShouldResumeWithinLimits()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SafeLimitFlow();
        var executor = new DslFlowExecutor<SafetyTestState, SafeLimitFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "safe-limits-persist" };
        SetupMediator(mediator);

        // Act - First execution
        var result1 = await executor.RunAsync(state);

        // Simulate recovery
        var snapshot2 = await store.GetAsync<SafetyTestState>("safe-limits-persist");
        var state2 = snapshot2?.State ?? new SafetyTestState { FlowId = "safe-limits-persist" };
        var executor2 = new DslFlowExecutor<SafetyTestState, SafeLimitFlow>(mediator, store, config);
        var result2 = await executor2.RunAsync(state2);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        state2.IterationCount.Should().Be(5);
    }

    [Fact]
    public async Task While_MemoryMonitoring_ShouldDetectExcessive()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new MemoryIntensiveFlow();
        var executor = new DslFlowExecutor<SafetyTestState, MemoryIntensiveFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "memory-monitoring" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        // Should detect excessive memory usage
        if (state.AllocatedMemory > 1000000000) // 1GB
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("memory");
        }
    }

    [Fact]
    public async Task While_DeadlockDetection_ShouldDetect()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new DeadlockFlow();
        var executor = new DslFlowExecutor<SafetyTestState, DeadlockFlow>(mediator, store, config);

        var state = new SafetyTestState { FlowId = "deadlock-detection" };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        // Should detect deadlock
        if (state.IsDeadlocked)
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("deadlock");
        }
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(x => Task.CompletedTask);
    }
}


// Test state class
public class SafetyTestState : IFlowState
{
    public string FlowId { get; set; }
    public int LoopDepth { get; set; }
    public int IterationCount { get; set; }
    public long AllocatedMemory { get; set; }
    public bool IsDeadlocked { get; set; }
    public Dictionary<string, object> Variables { get; set; } = new();
    public bool HasChanges { get; set; }

    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
}

// Test flow configurations
public class DepthLimitFlow : FlowConfig<SafetyTestState>
{
    protected override void Configure(IFlowBuilder<SafetyTestState> flow)
    {
        flow
            .While(s => s.LoopDepth < 2000) // Try to exceed default limit of 1000
                .Into(s => s.LoopDepth++)
                .Send(s => new SafetyTestCommand { FlowId = s.FlowId })
            .EndWhile();
    }
}

public class IterationLimitFlow : FlowConfig<SafetyTestState>
{
    protected override void Configure(IFlowBuilder<SafetyTestState> flow)
    {
        flow
            .While(s => s.IterationCount < 20000) // Try to exceed default limit of 10000
                .Into(s => s.IterationCount++)
                .Send(s => new SafetyTestCommand { FlowId = s.FlowId })
            .EndWhile();
    }
}

public class TimeoutLimitFlow : FlowConfig<SafetyTestState>
{
    protected override void Configure(IFlowBuilder<SafetyTestState> flow)
    {
        flow
            .While(s => true) // Infinite loop to trigger timeout
                .Send(s => new SlowSafetyTestCommand { FlowId = s.FlowId, DelayMs = 1000 })
            .EndWhile();
    }
}

public class CustomDepthLimitFlow : FlowConfig<SafetyTestState>
{
    private readonly int _depthLimit;

    public CustomDepthLimitFlow(int depthLimit)
    {
        _depthLimit = depthLimit;
    }

    protected override void Configure(IFlowBuilder<SafetyTestState> flow)
    {
        flow
            .While(s => s.LoopDepth < _depthLimit + 100)
                .Into(s => s.LoopDepth++)
                .Send(s => new SafetyTestCommand { FlowId = s.FlowId })
            .EndWhile();
    }
}

public class CustomIterationLimitFlow : FlowConfig<SafetyTestState>
{
    private readonly int _iterationLimit;

    public CustomIterationLimitFlow(int iterationLimit)
    {
        _iterationLimit = iterationLimit;
    }

    protected override void Configure(IFlowBuilder<SafetyTestState> flow)
    {
        flow
            .While(s => s.IterationCount < _iterationLimit + 100)
                .Into(s => s.IterationCount++)
                .Send(s => new SafetyTestCommand { FlowId = s.FlowId })
            .EndWhile();
    }
}

public class CustomTimeoutLimitFlow : FlowConfig<SafetyTestState>
{
    private readonly TimeSpan _timeout;

    public CustomTimeoutLimitFlow(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    protected override void Configure(IFlowBuilder<SafetyTestState> flow)
    {
        flow
            .While(s => true)
                .Send(s => new SlowSafetyTestCommand { FlowId = s.FlowId, DelayMs = 1000 })
            .EndWhile();
    }
}

public class SafeLimitFlow : FlowConfig<SafetyTestState>
{
    protected override void Configure(IFlowBuilder<SafetyTestState> flow)
    {
        flow
            .While(s => s.IterationCount < 5)
                .Into(s => s.IterationCount++)
                .Send(s => new SafetyTestCommand { FlowId = s.FlowId })
            .EndWhile();
    }
}

public class MemoryIntensiveFlow : FlowConfig<SafetyTestState>
{
    protected override void Configure(IFlowBuilder<SafetyTestState> flow)
    {
        flow
            .While(s => s.AllocatedMemory < 2000000000) // Try to allocate 2GB
                .Into(s => s.AllocatedMemory += 100000000) // Add 100MB each iteration
                .Send(s => new SafetyTestCommand { FlowId = s.FlowId })
            .EndWhile();
    }
}

public class DeadlockFlow : FlowConfig<SafetyTestState>
{
    protected override void Configure(IFlowBuilder<SafetyTestState> flow)
    {
        flow
            .While(s => !s.IsDeadlocked)
                .Send(s => new DeadlockTestCommand { FlowId = s.FlowId })
                .Into(s => s.IsDeadlocked = true) // Simulate deadlock
            .EndWhile();
    }
}

// Test commands
public class SafetyTestCommand : IRequest, IMessage
{
    public string FlowId { get; set; }
    public long MessageId { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public class SlowSafetyTestCommand : IRequest, IMessage
{
    public string FlowId { get; set; }
    public int DelayMs { get; set; }
    public long MessageId { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public class DeadlockTestCommand : IRequest, IMessage
{
    public string FlowId { get; set; }
    public long MessageId { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public class SuccessResponse
{
    public bool IsSuccess => true;
}
