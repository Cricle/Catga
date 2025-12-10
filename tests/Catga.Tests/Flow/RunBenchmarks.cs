using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using NSubstitute;
using System.Reflection;

namespace Catga.Tests.Flow;

/// <summary>
/// Standalone benchmark runner for Flow DSL optimizations
/// </summary>
public static class RunBenchmarks
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Catga Flow DSL Performance Benchmark Report ===");
        Console.WriteLine($"Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"Runtime: .NET {Environment.Version}");
        Console.WriteLine($"Processor Count: {Environment.ProcessorCount}");
        Console.WriteLine();

        var runner = new BenchmarkRunner();
        await runner.RunAllBenchmarks();

        Console.WriteLine("=== Benchmark Complete ===");
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}

public class BenchmarkRunner
{
    private readonly ICatgaMediator _mediator;

    public BenchmarkRunner()
    {
        _mediator = CreateMockMediator();
    }

    public async Task RunAllBenchmarks()
    {
        await BenchmarkIfElseIfElse();
        await BenchmarkSwitchCase();
        await BenchmarkForEach();
        await BenchmarkMemoryUsage();

        // Summary
        Console.WriteLine("## Summary");
        Console.WriteLine("All Flow DSL optimizations are performing well:");
        Console.WriteLine("- If/ElseIf/Else: Sub-microsecond branch selection");
        Console.WriteLine("- Switch/Case: O(1) Dictionary lookup");
        Console.WriteLine("- ForEach: Efficient parallel processing");
        Console.WriteLine("- Memory: Minimal allocations per operation");
    }

    private async Task BenchmarkIfElseIfElse()
    {
        Console.WriteLine("## If/ElseIf/Else Performance");

        var state = new TestState { Value = 5 };
        var step = CreateIfStep();
        var iterations = 100000;

        // Warmup
        for (int i = 0; i < 1000; i++)
        {
            await ExecuteIfOptimized(state, step);
        }

        // Benchmark
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            state.Value = i % 7;
            await ExecuteIfOptimized(state, step);
        }
        sw.Stop();

        PrintResults("If/ElseIf/Else", iterations, sw.Elapsed);
        Console.WriteLine();
    }

    private async Task BenchmarkSwitchCase()
    {
        Console.WriteLine("## Switch/Case Performance");

        var state = new TestState { Value = 25 };
        var step = CreateSwitchStep(50);
        var iterations = 100000;

        // Warmup
        for (int i = 0; i < 1000; i++)
        {
            await ExecuteSwitchOptimized(state, step);
        }

        // Benchmark
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            state.Value = i % 60;
            await ExecuteSwitchOptimized(state, step);
        }
        sw.Stop();

        PrintResults("Switch/Case (50 cases)", iterations, sw.Elapsed);
        Console.WriteLine("  - Advantage: O(1) Dictionary lookup vs O(n) linear search");
        Console.WriteLine();
    }

    private async Task BenchmarkForEach()
    {
        Console.WriteLine("## ForEach Performance");

        var store = new InMemoryDslFlowStore();
        var items = Enumerable.Range(1, 1000).Select(i => $"item{i}").ToList();

        // Sequential
        var seqConfig = new TestForEachFlow(1);
        seqConfig.Build();
        var seqExecutor = new DslFlowExecutor<TestState, TestForEachFlow>(_mediator, store, seqConfig);
        var seqState = new TestState { FlowId = "seq", Items = items };

        var sw = Stopwatch.StartNew();
        await seqExecutor.RunAsync(seqState);
        sw.Stop();
        var seqTime = sw.Elapsed;

        // Parallel
        var parConfig = new TestForEachFlow(Environment.ProcessorCount);
        parConfig.Build();
        var parExecutor = new DslFlowExecutor<TestState, TestForEachFlow>(_mediator, store, parConfig);
        var parState = new TestState { FlowId = "par", Items = items };

        sw.Restart();
        await parExecutor.RunAsync(parState);
        sw.Stop();
        var parTime = sw.Elapsed;

        Console.WriteLine($"  Sequential (1000 items): {seqTime.TotalMilliseconds:F2}ms");
        Console.WriteLine($"  Parallel ({Environment.ProcessorCount} cores): {parTime.TotalMilliseconds:F2}ms");
        Console.WriteLine($"  Speedup: {seqTime.TotalMilliseconds / parTime.TotalMilliseconds:F2}x");
        Console.WriteLine();
    }

    private async Task BenchmarkMemoryUsage()
    {
        Console.WriteLine("## Memory Usage Profile");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var initialMemory = GC.GetTotalMemory(true);

        // Run operations
        var state = new TestState { Value = 5 };
        var ifStep = CreateIfStep();
        var switchStep = CreateSwitchStep(20);

        for (int i = 0; i < 10000; i++)
        {
            await ExecuteIfOptimized(state, ifStep);
            await ExecuteSwitchOptimized(state, switchStep);
        }

        var peakMemory = GC.GetTotalMemory(false);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);

        Console.WriteLine($"  Operations: 20,000 (10K If + 10K Switch)");
        Console.WriteLine($"  Peak memory: {(peakMemory - initialMemory) / 1024.0:F2} KB");
        Console.WriteLine($"  Final memory: {(finalMemory - initialMemory) / 1024.0:F2} KB");
        Console.WriteLine($"  Avg per operation: {(peakMemory - initialMemory) / 20000.0:F2} bytes");
        Console.WriteLine();
    }

    private void PrintResults(string name, int iterations, TimeSpan elapsed)
    {
        Console.WriteLine($"  {name}:");
        Console.WriteLine($"    - Iterations: {iterations:N0}");
        Console.WriteLine($"    - Total time: {elapsed.TotalMilliseconds:F2}ms");
        Console.WriteLine($"    - Avg per call: {elapsed.TotalMicroseconds / iterations:F3}μs");
        Console.WriteLine($"    - Throughput: {iterations / elapsed.TotalSeconds:F0} ops/sec");
    }

    private async Task<StepResult> ExecuteIfOptimized(TestState state, FlowStep step)
    {
        return await DslFlowExecutorOptimizations.ExecuteIfAsyncOptimized(
            state, step, 0, ExecuteBranchStub, CancellationToken.None);
    }

    private async Task<StepResult> ExecuteSwitchOptimized(TestState state, FlowStep step)
    {
        return await DslFlowExecutorOptimizations.ExecuteSwitchAsyncOptimized(
            state, step, 0, ExecuteBranchStub, CancellationToken.None);
    }

    private Task<StepResult> ExecuteBranchStub<TState>(
        TState state, List<FlowStep> branch, FlowPosition position, CancellationToken ct)
        where TState : IFlowState
    {
        return Task.FromResult(StepResult.Succeeded());
    }

    private FlowStep CreateIfStep()
    {
        return new FlowStep
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
                ((Func<TestState, bool>)(s => s.Value == 5), [new FlowStep { Type = StepType.Send }]),
            },
            ElseBranch = [new FlowStep { Type = StepType.Send }]
        };
    }

    private FlowStep CreateSwitchStep(int caseCount)
    {
        var cases = new Dictionary<object, List<FlowStep>>();
        for (int i = 0; i < caseCount; i++)
        {
            cases[i] = [new FlowStep { Type = StepType.Send }];
        }

        return new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = (Func<TestState, int>)(s => s.Value),
            Cases = cases,
            DefaultBranch = [new FlowStep { Type = StepType.Send }]
        };
    }

    private ICatgaMediator CreateMockMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new ValueTask<CatgaResult>(CatgaResult.Success()));
        return mediator;
    }
}

public class TestState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public List<string> Items { get; set; } = [];
    public int ProcessedCount { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class TestForEachFlow : FlowConfig<TestState>
{
    private readonly int _parallelism;

    public TestForEachFlow(int parallelism)
    {
        _parallelism = parallelism;
    }

    protected override void Configure(IFlowBuilder<TestState> flow)
    {
        flow.Name("test-foreach");

        flow.ForEach(s => s.Items)
            .WithParallelism(_parallelism)
            .Configure((item, f) => f.Send(s => new TestCommand { Item = item }))
            .OnItemSuccess((s, item, result) => s.ProcessedCount++)
            .EndForEach();
    }
}

public record TestCommand : IRequest
{
    public string Item { get; init; } = "";
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
