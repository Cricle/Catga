using System.Diagnostics;
using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Tests.Flow.TDD;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Performance benchmarks for Flow DSL execution.
/// </summary>
public class FlowDslPerformanceBenchmarks
{
    [Fact]
    public async Task SimpleFlow_ExecutionTime_ShouldBeFast()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SimplePerformanceFlow();
        var executor = new DslFlowExecutor<PerformanceTestState, SimplePerformanceFlow>(mediator, store, config);
        var state = new PerformanceTestState { FlowId = "perf-simple" };

        var sw = Stopwatch.StartNew();

        // Act
        var result = await executor.RunAsync(state);

        sw.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        sw.ElapsedMilliseconds.Should().BeLessThan(100, "simple flow should execute in less than 100ms");
    }

    [Fact]
    public async Task LoopFlow_1000Iterations_ExecutionTime_ShouldBeAcceptable()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new LoopPerformanceFlow();
        var executor = new DslFlowExecutor<PerformanceTestState, LoopPerformanceFlow>(mediator, store, config);
        var state = new PerformanceTestState { FlowId = "perf-loop", Counter = 0 };

        var sw = Stopwatch.StartNew();

        // Act
        var result = await executor.RunAsync(state);

        sw.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        state.Counter.Should().Be(1000);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, "1000 iterations should complete in less than 5 seconds");
    }

    [Fact]
    public async Task NestedLoopFlow_ExecutionTime_ShouldBeAcceptable()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new NestedLoopPerformanceFlow();
        var executor = new DslFlowExecutor<PerformanceTestState, NestedLoopPerformanceFlow>(mediator, store, config);
        var state = new PerformanceTestState { FlowId = "perf-nested" };

        var sw = Stopwatch.StartNew();

        // Act
        var result = await executor.RunAsync(state);

        sw.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        sw.ElapsedMilliseconds.Should().BeLessThan(2000, "nested loops should complete in less than 2 seconds");
    }

    [Fact]
    public async Task BranchingFlow_ExecutionTime_ShouldBeFast()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new BranchingPerformanceFlow();
        var executor = new DslFlowExecutor<PerformanceTestState, BranchingPerformanceFlow>(mediator, store, config);
        var state = new PerformanceTestState { FlowId = "perf-branch", Value = 50 };

        var sw = Stopwatch.StartNew();

        // Act
        var result = await executor.RunAsync(state);

        sw.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        sw.ElapsedMilliseconds.Should().BeLessThan(100, "branching flow should execute in less than 100ms");
    }
}

/// <summary>
/// Test state for performance benchmarks.
/// </summary>
public class PerformanceTestState : TestStateBase
{
    public int Counter { get; set; }
    public int Value { get; set; }
}

/// <summary>
/// Simple flow for baseline performance.
/// </summary>
public class SimplePerformanceFlow : FlowConfig<PerformanceTestState>
{
    protected override void Configure(IFlowBuilder<PerformanceTestState> flow)
    {
        flow
            .Into(s => s.Counter = 1)
            .Into(s => s.Counter = 2)
            .Into(s => s.Counter = 3);
    }
}

/// <summary>
/// Loop flow for iteration performance.
/// </summary>
public class LoopPerformanceFlow : FlowConfig<PerformanceTestState>
{
    protected override void Configure(IFlowBuilder<PerformanceTestState> flow)
    {
        flow
            .While(s => s.Counter < 1000)
                .Into(s => s.Counter++)
            .EndWhile();
    }
}

/// <summary>
/// Nested loop flow for complex iteration.
/// </summary>
public class NestedLoopPerformanceFlow : FlowConfig<PerformanceTestState>
{
    protected override void Configure(IFlowBuilder<PerformanceTestState> flow)
    {
        flow
            .Repeat(10)
                .While(s => s.Counter < 10)
                    .Into(s => s.Counter++)
                .EndWhile()
                .Into(s => s.Counter = 0)
            .EndRepeat();
    }
}

/// <summary>
/// Branching flow for conditional performance.
/// </summary>
public class BranchingPerformanceFlow : FlowConfig<PerformanceTestState>
{
    protected override void Configure(IFlowBuilder<PerformanceTestState> flow)
    {
        flow
            .If(s => s.Value > 100)
                .Into(s => s.Counter = 1)
            .ElseIf(s => s.Value > 50)
                .Into(s => s.Counter = 2)
            .ElseIf(s => s.Value > 25)
                .Into(s => s.Counter = 3)
            .Else(f => f.Into(s => s.Counter = 4))
            .EndIf();
    }
}
