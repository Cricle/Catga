using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Flow;

/// <summary>
/// Comprehensive benchmarks for Flow DSL executor optimizations
/// </summary>
public class FlowExecutorBenchmarks
{
    private readonly ITestOutputHelper _output;
    private readonly ICatgaMediator _mediator;

    public FlowExecutorBenchmarks(ITestOutputHelper output)
    {
        _output = output;
        _mediator = CreateMockMediator();
    }

    [Fact]
    public async Task Benchmark_AllOptimizations_ComprehensiveReport()
    {
        _output.WriteLine("=== Flow DSL Performance Benchmark Report ===");
        _output.WriteLine($"Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        _output.WriteLine("");

        // Benchmark If/ElseIf/Else
        await BenchmarkIfElseIfElse();

        // Benchmark Switch/Case
        await BenchmarkSwitchCase();

        // Benchmark ForEach (Sequential vs Parallel)
        await BenchmarkForEach();

        // Benchmark Nested Flows
        await BenchmarkNestedFlows();

        // Benchmark Memory Usage
        await BenchmarkMemoryUsage();

        _output.WriteLine("");
        _output.WriteLine("=== Benchmark Complete ===");
    }

    private async Task BenchmarkIfElseIfElse()
    {
        _output.WriteLine("## If/ElseIf/Else Performance");

        var metrics = new InMemoryFlowMetrics();
        var state = new BenchmarkState { Value = 5 };

        // Create complex If step with multiple ElseIf branches
        var step = new FlowStep
        {
            Type = StepType.If,
            BranchCondition = (Func<BenchmarkState, bool>)(s => s.Value == 0),
            ThenBranch = [new FlowStep { Type = StepType.Send }],
            ElseIfBranches = new List<(Delegate, List<FlowStep>)>
            {
                ((Func<BenchmarkState, bool>)(s => s.Value == 1), [new FlowStep { Type = StepType.Send }]),
                ((Func<BenchmarkState, bool>)(s => s.Value == 2), [new FlowStep { Type = StepType.Send }]),
                ((Func<BenchmarkState, bool>)(s => s.Value == 3), [new FlowStep { Type = StepType.Send }]),
                ((Func<BenchmarkState, bool>)(s => s.Value == 4), [new FlowStep { Type = StepType.Send }]),
                ((Func<BenchmarkState, bool>)(s => s.Value == 5), [new FlowStep { Type = StepType.Send }]),
            },
            ElseBranch = [new FlowStep { Type = StepType.Send }]
        };

        var iterations = 10000;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            state.Value = i % 7; // Vary the branch taken
            await DslFlowExecutorOptimizations.ExecuteIfAsyncWithMetrics(
                state, step, 0, ExecuteBranchStub, CancellationToken.None, metrics);
        }

        stopwatch.Stop();

        _output.WriteLine($"  - Iterations: {iterations:N0}");
        _output.WriteLine($"  - Total time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  - Avg per execution: {stopwatch.Elapsed.TotalMicroseconds / iterations:F2}μs");
        _output.WriteLine($"  - Throughput: {iterations / stopwatch.Elapsed.TotalSeconds:F0} ops/sec");
        _output.WriteLine("");
    }

    private async Task BenchmarkSwitchCase()
    {
        _output.WriteLine("## Switch/Case Performance");

        var metrics = new InMemoryFlowMetrics();
        var state = new BenchmarkState { Value = 10 };

        // Create Switch with many cases
        var cases = new Dictionary<object, List<FlowStep>>();
        for (int i = 0; i < 50; i++)
        {
            cases[i] = [new FlowStep { Type = StepType.Send }];
        }

        var step = new FlowStep
        {
            Type = StepType.Switch,
            SwitchSelector = (Func<BenchmarkState, int>)(s => s.Value),
            Cases = cases,
            DefaultBranch = [new FlowStep { Type = StepType.Send }]
        };

        var iterations = 10000;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            state.Value = i % 60; // Some hit cases, some hit default
            await DslFlowExecutorOptimizations.ExecuteSwitchAsyncWithMetrics(
                state, step, 0, ExecuteBranchStub, CancellationToken.None, metrics);
        }

        stopwatch.Stop();

        _output.WriteLine($"  - Cases: 50");
        _output.WriteLine($"  - Iterations: {iterations:N0}");
        _output.WriteLine($"  - Total time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  - Avg per execution: {stopwatch.Elapsed.TotalMicroseconds / iterations:F2}μs");
        _output.WriteLine($"  - Throughput: {iterations / stopwatch.Elapsed.TotalSeconds:F0} ops/sec");
        _output.WriteLine($"  - O(1) lookup advantage: Dictionary-based implementation");
        _output.WriteLine("");
    }

    private async Task BenchmarkForEach()
    {
        _output.WriteLine("## ForEach Performance (Sequential vs Parallel)");

        var store = new InMemoryDslFlowStore();
        var config = new BenchmarkForEachFlow();
        config.Build();

        var state = new BenchmarkState
        {
            FlowId = "foreach-benchmark",
            Items = Enumerable.Range(1, 1000).Select(i => $"item{i}").ToList()
        };

        // Sequential
        config.MaxParallelism = 1;
        var executor = new DslFlowExecutor<BenchmarkState, BenchmarkForEachFlow>(_mediator, store, config);

        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        _output.WriteLine($"  Sequential (1000 items):");
        _output.WriteLine($"    - Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"    - Items/sec: {1000 / stopwatch.Elapsed.TotalSeconds:F0}");

        // Parallel
        state.ProcessedCount = 0;
        config.MaxParallelism = Environment.ProcessorCount;
        executor = new DslFlowExecutor<BenchmarkState, BenchmarkForEachFlow>(_mediator, store, config);

        stopwatch.Restart();
        result = await executor.RunAsync(state);
        stopwatch.Stop();

        _output.WriteLine($"  Parallel ({Environment.ProcessorCount} cores, 1000 items):");
        _output.WriteLine($"    - Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"    - Items/sec: {1000 / stopwatch.Elapsed.TotalSeconds:F0}");
        _output.WriteLine($"    - Speedup: ~{Environment.ProcessorCount}x theoretical max");
        _output.WriteLine("");
    }

    private async Task BenchmarkNestedFlows()
    {
        _output.WriteLine("## Nested Flow Performance");

        var store = new InMemoryDslFlowStore();
        var config = new NestedBenchmarkFlow();
        config.Build();

        var state = new BenchmarkState
        {
            FlowId = "nested-benchmark",
            Items = Enumerable.Range(1, 100).Select(i => $"item{i}").ToList()
        };

        var executor = new DslFlowExecutor<BenchmarkState, NestedBenchmarkFlow>(_mediator, store, config);

        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        _output.WriteLine($"  - Nesting depth: 3 levels");
        _output.WriteLine($"  - Total operations: 100 items × 3 branches");
        _output.WriteLine($"  - Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  - Avg per nested operation: {stopwatch.Elapsed.TotalMicroseconds / 300:F2}μs");
        _output.WriteLine("");
    }

    private async Task BenchmarkMemoryUsage()
    {
        _output.WriteLine("## Memory Usage Profile");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var initialMemory = GC.GetTotalMemory(true);

        // Run a complex flow
        var store = new InMemoryDslFlowStore();
        var config = new ComplexBenchmarkFlow();
        config.Build();

        var state = new BenchmarkState
        {
            FlowId = "memory-benchmark",
            Items = Enumerable.Range(1, 1000).Select(i => $"item{i}").ToList()
        };

        var executor = new DslFlowExecutor<BenchmarkState, ComplexBenchmarkFlow>(_mediator, store, config);
        var result = await executor.RunAsync(state);

        var finalMemory = GC.GetTotalMemory(false);
        var memoryUsed = finalMemory - initialMemory;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var afterGcMemory = GC.GetTotalMemory(true);
        var persistentMemory = afterGcMemory - initialMemory;

        _output.WriteLine($"  - Peak memory usage: {memoryUsed / 1024.0:F2} KB");
        _output.WriteLine($"  - Memory after GC: {persistentMemory / 1024.0:F2} KB");
        _output.WriteLine($"  - Memory per item: {memoryUsed / 1000.0:F2} bytes");
        _output.WriteLine($"  - GC Collections (Gen 0/1/2): {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
        _output.WriteLine("");
    }

    private Task<StepResult> ExecuteBranchStub<TState>(
        TState state, List<FlowStep> branch, FlowPosition position, CancellationToken ct)
        where TState : IFlowState
    {
        return Task.FromResult(StepResult.Succeeded());
    }

    private ICatgaMediator CreateMockMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new ValueTask<CatgaResult>(CatgaResult.Success()));
        return mediator;
    }
}

// Benchmark state
public class BenchmarkState : IFlowState
{
    public string? FlowId { get; set; }
    public int Value { get; set; }
    public List<string> Items { get; set; } = [];
    public int ProcessedCount { get; set; }

    // IFlowState implementation
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Benchmark flows
public class BenchmarkForEachFlow : FlowConfig<BenchmarkState>
{
    public int MaxParallelism { get; set; } = 1;

    protected override void Configure(IFlowBuilder<BenchmarkState> flow)
    {
        flow.Name("benchmark-foreach");

        flow.ForEach(s => s.Items)
            .WithParallelism(MaxParallelism)
            .Configure((item, f) => f.Send(s => new BenchmarkCommand { Item = item }))
            .OnItemSuccess((s, item, result) => s.ProcessedCount++)
            .EndForEach();
    }
}

public class NestedBenchmarkFlow : FlowConfig<BenchmarkState>
{
    protected override void Configure(IFlowBuilder<BenchmarkState> flow)
    {
        flow.Name("nested-benchmark");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.If(s => s.Value > 0)
                    .Send(s => new BenchmarkCommand { Item = item })
                    .ElseIf(s => s.Value == 0)
                    .Send(s => new BenchmarkCommand { Item = item + "-zero" })
                    .Else()
                    .Send(s => new BenchmarkCommand { Item = item + "-negative" })
                    .EndIf();
            })
            .EndForEach();
    }
}

public class ComplexBenchmarkFlow : FlowConfig<BenchmarkState>
{
    protected override void Configure(IFlowBuilder<BenchmarkState> flow)
    {
        flow.Name("complex-benchmark");

        // Complex flow with multiple constructs
        var ifBuilder = flow.If(s => s.Items.Count > 0);
        ifBuilder.ForEach(s => s.Items.Take(100))
            .WithParallelism(4)
            .Configure((item, f) =>
            {
                f.Switch(s => s.Value % 3)
                    .Case(0, f2 => f2.Send(s => new BenchmarkCommand { Item = item + "-A" }))
                    .Case(1, f2 => f2.Send(s => new BenchmarkCommand { Item = item + "-B" }))
                    .Default(f2 => f2.Send(s => new BenchmarkCommand { Item = item + "-C" }))
                    .EndSwitch();
            })
            .EndForEach();
        ifBuilder.EndIf();
    }
}

public record BenchmarkCommand : IRequest
{
    public string Item { get; init; } = "";
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
