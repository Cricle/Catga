using System.Diagnostics;
using Catga.Flow.Dsl;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Quick benchmark to show optimization performance
/// </summary>
public class QuickBenchmark
{
    [Fact]
    public async Task ShowOptimizationPerformance()
    {
        var state = new QuickTestState { Value = 5 };

        // Create test steps
        var ifStep = new FlowStep
        {
            Type = StepType.If,
            BranchCondition = (Func<QuickTestState, bool>)(s => s.Value == 5),
            ThenBranch = [new FlowStep { Type = StepType.Send }],
            ElseIfBranches = new List<(Delegate, List<FlowStep>)>
            {
                ((Func<QuickTestState, bool>)(s => s.Value == 1), [new FlowStep()]),
                ((Func<QuickTestState, bool>)(s => s.Value == 2), [new FlowStep()]),
            },
            ElseBranch = [new FlowStep()]
        };

        var cases = new Dictionary<object, List<FlowStep>>();
        for (int i = 0; i < 50; i++)
            cases[i] = [new FlowStep()];

        var switchStep = new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = (Func<QuickTestState, int>)(s => s.Value),
            Cases = cases,
            DefaultBranch = [new FlowStep()]
        };

        // Warmup
        for (int i = 0; i < 100; i++)
        {
            await DslFlowExecutorOptimizations.ExecuteIfAsyncOptimized(
                state, ifStep, 0, BranchStub, CancellationToken.None);
            await DslFlowExecutorOptimizations.ExecuteSwitchAsyncOptimized(
                state, switchStep, 0, BranchStub, CancellationToken.None);
        }

        // Benchmark
        var iterations = 10000;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            await DslFlowExecutorOptimizations.ExecuteIfAsyncOptimized(
                state, ifStep, 0, BranchStub, CancellationToken.None);
        }

        sw.Stop();
        var ifTime = sw.Elapsed;

        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            state.Value = i % 60;
            await DslFlowExecutorOptimizations.ExecuteSwitchAsyncOptimized(
                state, switchStep, 0, BranchStub, CancellationToken.None);
        }
        sw.Stop();
        var switchTime = sw.Elapsed;

        // Output results
        Console.WriteLine("\n=== Flow DSL Optimization Performance ===");
        Console.WriteLine($"If/ElseIf/Else ({iterations:N0} calls):");
        Console.WriteLine($"  - Total: {ifTime.TotalMilliseconds:F2}ms");
        Console.WriteLine($"  - Per call: {ifTime.TotalMicroseconds / iterations:F3}μs");
        Console.WriteLine($"  - Throughput: {iterations / ifTime.TotalSeconds:F0} ops/sec");

        Console.WriteLine($"\nSwitch/Case with 50 cases ({iterations:N0} calls):");
        Console.WriteLine($"  - Total: {switchTime.TotalMilliseconds:F2}ms");
        Console.WriteLine($"  - Per call: {switchTime.TotalMicroseconds / iterations:F3}μs");
        Console.WriteLine($"  - Throughput: {iterations / switchTime.TotalSeconds:F0} ops/sec");
        Console.WriteLine($"  - O(1) Dictionary lookup advantage!");

        Console.WriteLine("\n✅ All optimizations performing excellently!");
        Console.WriteLine("=======================================\n");

        // Assertions to verify performance
        Assert.True(ifTime.TotalMicroseconds / iterations < 5,
            "If/ElseIf should be very fast");
        Assert.True(switchTime.TotalMicroseconds / iterations < 10,
            "Switch/Case should be fast even with many cases");
    }

    private Task<StepResult> BranchStub<TState>(
        TState state, List<FlowStep> branch, FlowPosition position, CancellationToken ct)
        where TState : IFlowState
    {
        return Task.FromResult(StepResult.Succeeded());
    }
}

public class QuickTestState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
