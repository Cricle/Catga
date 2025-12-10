using System.Diagnostics;
using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Flow;

/// <summary>
/// Performance comparison tests for ExecuteIfAsync optimizations
/// </summary>
public class ExecuteIfOptimizationTests
{
    private readonly ITestOutputHelper _output;

    public ExecuteIfOptimizationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public async Task CompareExecuteIfPerformance(int iterations)
    {
        // Arrange
        var state = new TestState { Value = 5 };
        var metrics = new InMemoryFlowMetrics();

        // Create a complex If step with multiple ElseIf branches
        var step = new FlowStep
        {
            Type = StepType.If,
            BranchCondition = (Func<TestState, bool>)(s => s.Value == 0),
            ThenBranch = [new FlowStep { Type = StepType.Send }],
            ElseIfBranches = new List<(Delegate, List<FlowStep>)>
            {
                ((Func<TestState, bool>)(s => s.Value == 1), [new FlowStep { Type = StepType.Send }]),
                ((Func<TestState, bool>)(s => s.Value == 2), [new FlowStep { Type = StepType.Send }]),
                ((Func<TestState, bool>)(s => s.Value == 3), [new FlowStep { Type = StepType.Send }]),
                ((Func<TestState, bool>)(s => s.Value == 4), [new FlowStep { Type = StepType.Send }]),
                ((Func<TestState, bool>)(s => s.Value == 5), [new FlowStep { Type = StepType.Send }]), // This will match
                ((Func<TestState, bool>)(s => s.Value == 6), [new FlowStep { Type = StepType.Send }]),
                ((Func<TestState, bool>)(s => s.Value == 7), [new FlowStep { Type = StepType.Send }]),
            },
            ElseBranch = [new FlowStep { Type = StepType.Send }]
        };

        // Warm up
        for (int i = 0; i < 10; i++)
        {
            await DslFlowExecutorOptimizations.ExecuteIfAsyncOptimized(
                state, step, 0, ExecuteBranchStub, CancellationToken.None);
        }

        // Act - Measure optimized version
        var optimizedStopwatch = Stopwatch.StartNew();
        var optimizedInitialMemory = GC.GetTotalMemory(true);

        for (int i = 0; i < iterations; i++)
        {
            await DslFlowExecutorOptimizations.ExecuteIfAsyncWithMetrics(
                state, step, 0, ExecuteBranchStub, CancellationToken.None, metrics);
        }

        optimizedStopwatch.Stop();
        var optimizedMemoryUsed = GC.GetTotalMemory(false) - optimizedInitialMemory;

        // Assert and output results
        _output.WriteLine($"Iterations: {iterations}");
        _output.WriteLine($"Optimized Time: {optimizedStopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Optimized Memory: {optimizedMemoryUsed / 1024.0:F2} KB");
        _output.WriteLine($"Avg Time per call: {optimizedStopwatch.Elapsed.TotalMicroseconds / iterations:F2} μs");

        // Performance assertions
        var avgTimePerCall = optimizedStopwatch.Elapsed.TotalMicroseconds / iterations;
        avgTimePerCall.Should().BeLessThan(200, "Each If execution should be fast (under 200μs)");

        // Check metrics were collected
        metrics.IfMetrics.Should().HaveCount(iterations);
        metrics.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteIfOptimized_ShouldHandleAllBranchTypes()
    {
        // Test Then branch
        await TestBranch(0, true, false, false, false);

        // Test ElseIf branches
        await TestBranch(1, false, true, false, false);
        await TestBranch(2, false, false, true, false);

        // Test Else branch
        await TestBranch(-1, false, false, false, true);
    }

    private async Task TestBranch(int expectedBranchIndex, bool thenCondition,
        bool elseIf1Condition, bool elseIf2Condition, bool useElse)
    {
        var state = new TestState();
        var actualBranchIndex = -100;

        var step = new FlowStep
        {
            Type = StepType.If,
            BranchCondition = (Func<TestState, bool>)(s => thenCondition),
            ThenBranch = [new FlowStep { Type = StepType.Send }],
            ElseIfBranches = new List<(Delegate, List<FlowStep>)>
            {
                ((Func<TestState, bool>)(s => elseIf1Condition), [new FlowStep { Type = StepType.Send }]),
                ((Func<TestState, bool>)(s => elseIf2Condition), [new FlowStep { Type = StepType.Send }]),
            },
            ElseBranch = useElse ? [new FlowStep { Type = StepType.Send }] : null
        };

        var result = await DslFlowExecutorOptimizations.ExecuteIfAsyncOptimized(
            state, step, 0,
            (s, branch, position, ct) =>
            {
                actualBranchIndex = position.Path[1];
                return Task.FromResult(StepResult.Succeeded());
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        if (expectedBranchIndex != -100)
        {
            actualBranchIndex.Should().Be(expectedBranchIndex,
                $"Should execute the correct branch for conditions: then={thenCondition}, elseIf1={elseIf1Condition}, elseIf2={elseIf2Condition}, else={useElse}");
        }
    }

    [Fact]
    public async Task ExecuteIfOptimized_ShouldHandleNullCondition()
    {
        var state = new TestState();
        var step = new FlowStep
        {
            Type = StepType.If,
            BranchCondition = null
        };

        var result = await DslFlowExecutorOptimizations.ExecuteIfAsyncOptimized(
            state, step, 0, ExecuteBranchStub, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("no condition");
    }

    [Fact]
    public async Task Metrics_ShouldCaptureExecutionDetails()
    {
        var state = new TestState { Value = 1 };
        var metrics = new InMemoryFlowMetrics();
        var step = new FlowStep
        {
            Type = StepType.If,
            BranchCondition = (Func<TestState, bool>)(s => s.Value == 1),
            ThenBranch = [new FlowStep { Type = StepType.Send }]
        };

        // Act
        await DslFlowExecutorOptimizations.ExecuteIfAsyncWithMetrics(
            state, step, 0, ExecuteBranchStub, CancellationToken.None, metrics);

        // Assert
        metrics.IfMetrics.Should().HaveCount(1);
        var metric = metrics.IfMetrics[0];
        metric.Success.Should().BeTrue();
        metric.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        metric.MemoryUsed.Should().BeGreaterThanOrEqualTo(0);
    }

    private Task<StepResult> ExecuteBranchStub<TState>(
        TState state, List<FlowStep> branch, FlowPosition position, CancellationToken ct)
        where TState : IFlowState
    {
        return Task.FromResult(StepResult.Succeeded());
    }

    private class TestState : IFlowState
    {
        public string? FlowId { get; set; }
        public int Value { get; set; }

        // IFlowState implementation
        public bool HasChanges => true;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }
}
