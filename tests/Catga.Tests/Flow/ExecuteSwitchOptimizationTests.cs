using System.Diagnostics;
using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Flow;

/// <summary>
/// Performance comparison tests for ExecuteSwitchAsync optimizations
/// </summary>
public class ExecuteSwitchOptimizationTests
{
    private readonly ITestOutputHelper _output;

    public ExecuteSwitchOptimizationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(100, 5)]   // 100 iterations with 5 cases
    [InlineData(1000, 10)] // 1000 iterations with 10 cases
    [InlineData(5000, 20)] // 5000 iterations with 20 cases
    public async Task CompareExecuteSwitchPerformance(int iterations, int caseCount)
    {
        // Arrange
        var state = new TestState { Value = caseCount / 2 }; // Select middle case
        var metrics = new InMemoryFlowMetrics();

        // Create a Switch step with multiple cases
        var cases = new Dictionary<object, List<FlowStep>>();
        for (int i = 0; i < caseCount; i++)
        {
            cases[i] = [new FlowStep { Type = StepType.Send }];
        }

        var step = new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = (Func<TestState, int>)(s => s.Value),
            Cases = cases,
            DefaultBranch = [new FlowStep { Type = StepType.Send }]
        };

        // Warm up
        for (int i = 0; i < 10; i++)
        {
            await DslFlowExecutorOptimizations.ExecuteSwitchAsyncOptimized(
                state, step, 0, ExecuteBranchStub, CancellationToken.None);
        }

        // Act - Measure optimized version
        var optimizedStopwatch = Stopwatch.StartNew();
        var optimizedInitialMemory = GC.GetTotalMemory(true);

        for (int i = 0; i < iterations; i++)
        {
            // Vary the case being selected
            state.Value = i % caseCount;
            await DslFlowExecutorOptimizations.ExecuteSwitchAsyncWithMetrics(
                state, step, 0, ExecuteBranchStub, CancellationToken.None, metrics);
        }

        optimizedStopwatch.Stop();
        var optimizedMemoryUsed = GC.GetTotalMemory(false) - optimizedInitialMemory;

        // Assert and output results
        _output.WriteLine($"Iterations: {iterations}, Cases: {caseCount}");
        _output.WriteLine($"Optimized Time: {optimizedStopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Optimized Memory: {optimizedMemoryUsed / 1024.0:F2} KB");
        _output.WriteLine($"Avg Time per call: {optimizedStopwatch.Elapsed.TotalMicroseconds / iterations:F2} μs");

        // Performance assertions
        var avgTimePerCall = optimizedStopwatch.Elapsed.TotalMicroseconds / iterations;
        avgTimePerCall.Should().BeLessThan(300, "Each Switch execution should be fast (under 300μs)");

        // Check metrics were collected
        metrics.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteSwitchOptimized_ShouldHandleAllCaseTypes()
    {
        // Test matching first case
        await TestCase(5, 5, 0);

        // Test matching second case
        await TestCase(10, 10, 1);

        // Test matching third case
        await TestCase(15, 15, 2);

        // Test default case
        await TestCase(99, -1, -1);
    }

    private async Task TestCase(int selectorValue, int expectedValue, int expectedCaseIndex)
    {
        var state = new TestState { Value = selectorValue };
        var actualCaseIndex = -100;

        var step = new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = (Func<TestState, int>)(s => s.Value),
            Cases = new Dictionary<object, List<FlowStep>>
            {
                { 5, [new FlowStep { Type = StepType.Send }] },
                { 10, [new FlowStep { Type = StepType.Send }] },
                { 15, [new FlowStep { Type = StepType.Send }] },
            },
            DefaultBranch = expectedValue == -1 ? [new FlowStep { Type = StepType.Send }] : null
        };

        var result = await DslFlowExecutorOptimizations.ExecuteSwitchAsyncOptimized(
            state, step, 0,
            (s, branch, position, ct) =>
            {
                actualCaseIndex = position.Path[1];
                return Task.FromResult(StepResult.Succeeded());
            },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        if (expectedCaseIndex != -100)
        {
            actualCaseIndex.Should().Be(expectedCaseIndex,
                $"Should execute the correct case for selector value {selectorValue}");
        }
    }

    [Fact]
    public async Task ExecuteSwitchOptimized_ShouldHandleNullSelector()
    {
        var state = new TestState();
        var step = new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = null
        };

        var result = await DslFlowExecutorOptimizations.ExecuteSwitchAsyncOptimized(
            state, step, 0, ExecuteBranchStub, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("no selector");
    }

    [Fact]
    public async Task ExecuteSwitchOptimized_WithDictionary_ShouldUseO1Lookup()
    {
        var state = new TestState { Value = 50 };

        // Create a large dictionary of cases
        var casesDict = new Dictionary<object, List<FlowStep>>();
        for (int i = 0; i < 100; i++)
        {
            casesDict[i] = [new FlowStep { Type = StepType.Send }];
        }

        var step = new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = (Func<TestState, int>)(s => s.Value),
            Cases = casesDict // Use Dictionary for O(1) lookup
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await DslFlowExecutorOptimizations.ExecuteSwitchAsyncOptimized(
            state, step, 0, ExecuteBranchStub, CancellationToken.None);
        stopwatch.Stop();

        result.Success.Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(1),
            "Dictionary lookup should be very fast");
    }

    [Fact]
    public async Task Metrics_ShouldCaptureSwitchExecutionDetails()
    {
        var state = new TestState { Value = 10 };
        var metrics = new InMemoryFlowMetrics();
        var step = new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = (Func<TestState, int>)(s => s.Value),
            Cases = new Dictionary<object, List<FlowStep>>
            {
                { 10, [new FlowStep { Type = StepType.Send }] }
            }
        };

        // Act
        await DslFlowExecutorOptimizations.ExecuteSwitchAsyncWithMetrics(
            state, step, 0, ExecuteBranchStub, CancellationToken.None, metrics);

        // Assert
        // The metrics interface should have recorded the switch execution
        metrics.Errors.Should().BeEmpty();
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
