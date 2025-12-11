using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Engines;
using Catga.Flow.Dsl;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.Benchmarks;

/// <summary>
/// Comprehensive benchmarks comparing Catga with multiple workflow frameworks
/// </summary>
[Config(typeof(ComprehensiveBenchmarkConfig))]
[MemoryDiagnoser]
[ThreadingDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3)]
[SimpleJob(RuntimeMoniker.Net80, warmupCount: 5, iterationCount: 20)]
[RPlotExporter]
public class ComprehensiveBenchmarks
{
    private IServiceProvider? _catgaProvider;
    private readonly Consumer _consumer = new Consumer();

    [Params(1, 10, 100, 1000)]
    public int WorkflowCount { get; set; }

    [Params(10, 50, 100)]
    public int StepsPerWorkflow { get; set; }

    [Params(1, 5, 10)]
    public int BranchingFactor { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddFlowDsl();

        // Register various flow types
        services.AddFlow<BenchmarkState, LinearFlow>();
        services.AddFlow<BenchmarkState, BranchingFlow>();
        services.AddFlow<BenchmarkState, ParallelFlow>();
        services.AddFlow<BenchmarkState, ComplexFlow>();
        services.AddFlow<BenchmarkState, BenchmarkCompensationFlow>();
        services.AddFlow<BenchmarkState, HeavyStateFlow>();

        _catgaProvider = services.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_catgaProvider as IDisposable)?.Dispose();
    }

    #region Linear Workflow Benchmarks

    [Benchmark(Baseline = true)]
    public async Task<BenchmarkState> Catga_LinearWorkflow()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<BenchmarkState, LinearFlow>>();
        var state = new BenchmarkState
        {
            FlowId = Guid.NewGuid().ToString(),
            StepCount = StepsPerWorkflow
        };

        var result = await executor!.RunAsync(state).ConfigureAwait(false);
        return result.State;
    }

    [Benchmark]
    public BenchmarkState DirectExecution_LinearWorkflow()
    {
        var state = new BenchmarkState
        {
            FlowId = Guid.NewGuid().ToString(),
            StepCount = StepsPerWorkflow
        };

        // Direct execution without any framework
        for (int i = 0; i < StepsPerWorkflow; i++)
        {
            state.ExecutedSteps.Add($"step-{i}");
            state.Counter++;
        }

        state.Completed = true;
        return state;
    }

    #endregion

    #region Branching Workflow Benchmarks

    [Benchmark]
    public async Task<BenchmarkState> Catga_BranchingWorkflow()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<BenchmarkState, BranchingFlow>>();
        var state = new BenchmarkState
        {
            FlowId = Guid.NewGuid().ToString(),
            BranchSelector = Random.Shared.Next(0, BranchingFactor)
        };

        var result = await executor!.RunAsync(state).ConfigureAwait(false);
        return result.State;
    }

    #endregion

    #region Parallel Workflow Benchmarks

    [Benchmark]
    public async Task<BenchmarkState> Catga_ParallelWorkflow()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<BenchmarkState, ParallelFlow>>();
        var state = new BenchmarkState
        {
            FlowId = Guid.NewGuid().ToString(),
            Items = Enumerable.Range(0, StepsPerWorkflow).Select(i => $"item-{i}").ToList()
        };

        var result = await executor!.RunAsync(state).ConfigureAwait(false);
        return result.State;
    }

    [Benchmark]
    public async Task<BenchmarkState> TaskParallel_Workflow()
    {
        var state = new BenchmarkState
        {
            FlowId = Guid.NewGuid().ToString(),
            Items = Enumerable.Range(0, StepsPerWorkflow).Select(i => $"item-{i}").ToList()
        };

        // Using Task.WhenAll directly
        var tasks = state.Items.Select(async item =>
        {
            await Task.Yield();
            lock (state.ProcessedItems)
            {
                state.ProcessedItems.Add(item);
            }
        });

        await Task.WhenAll(tasks);
        state.Completed = true;
        return state;
    }

    #endregion

    #region Complex Workflow Benchmarks

    [Benchmark]
    public async Task<BenchmarkState> Catga_ComplexWorkflow()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<BenchmarkState, ComplexFlow>>();
        var state = new BenchmarkState
        {
            FlowId = Guid.NewGuid().ToString(),
            Items = Enumerable.Range(0, 10).Select(i => $"item-{i}").ToList(),
            BranchSelector = Random.Shared.Next(0, 3)
        };

        var result = await executor!.RunAsync(state).ConfigureAwait(false);
        return result.State;
    }

    #endregion

    #region Compensation Workflow Benchmarks

    [Benchmark]
    public async Task<BenchmarkState> Catga_CompensationWorkflow()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<BenchmarkState, BenchmarkCompensationFlow>>();
        var state = new BenchmarkState
        {
            FlowId = Guid.NewGuid().ToString(),
            ShouldFail = Random.Shared.Next(0, 2) == 0
        };

        try
        {
            var result = await executor!.RunAsync(state).ConfigureAwait(false);
            return result.State;
        }
        catch
        {
            // Compensation was executed
            return state;
        }
    }

    #endregion

    #region Heavy State Benchmarks

    [Benchmark]
    public async Task<BenchmarkState> Catga_HeavyStateWorkflow()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<BenchmarkState, HeavyStateFlow>>();
        var state = new BenchmarkState
        {
            FlowId = Guid.NewGuid().ToString(),
            LargeData = new byte[10 * 1024], // 10KB
            Dictionary = Enumerable.Range(0, 100)
                .ToDictionary(i => $"key-{i}", i => (object)$"value-{i}")
        };

        var result = await executor!.RunAsync(state).ConfigureAwait(false);
        return result.State;
    }

    #endregion

    #region Concurrent Workflows Benchmark

    [Benchmark]
    [Arguments(10)]
    [Arguments(50)]
    [Arguments(100)]
    public async Task Catga_ConcurrentWorkflows(int concurrency)
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<BenchmarkState, LinearFlow>>();
        var semaphore = new SemaphoreSlim(concurrency);

        var tasks = Enumerable.Range(0, WorkflowCount).Select(async i =>
        {
            await semaphore.WaitAsync();
            try
            {
                var state = new BenchmarkState
                {
                    FlowId = $"concurrent-{i}",
                    StepCount = 10
                };

                await executor!.RunAsync(state).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    #endregion

    #region Throughput Benchmarks

    [Benchmark]
    public void Catga_Throughput()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<BenchmarkState, LinearFlow>>();

        Parallel.For(0, WorkflowCount, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        }, i =>
        {
            var state = new BenchmarkState
            {
                FlowId = $"throughput-{i}",
                StepCount = 5
            };

            executor!.RunAsync(state).GetAwaiter().GetResult();
        });
    }

    #endregion
}

public class ComprehensiveBenchmarkConfig : ManualConfig
{
    public ComprehensiveBenchmarkConfig()
    {
        AddColumn(TargetMethodColumn.Method);
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.P95);
        AddColumn(StatisticColumn.P99);
        AddColumn(BaselineRatioColumn.RatioMean);
        AddColumn(RankColumn.Arabic);

        AddDiagnoser(MemoryDiagnoser.Default);
        AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig()));

        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);
        AddExporter(RPlotExporter.Default);
        AddExporter(CsvExporter.Default);
        AddExporter(JsonExporter.Brief);

        WithOptions(ConfigOptions.DisableOptimizationsValidator);
        WithOptions(ConfigOptions.JoinSummary);
    }
}

// Flow configurations for benchmarks
public class LinearFlow : FlowConfig<BenchmarkState>
{
    protected override void Configure(IFlowBuilder<BenchmarkState> flow)
    {
        for (int i = 0; i < 10; i++)
        {
            var stepIndex = i;
            flow.Step($"step-{stepIndex}", s =>
            {
                s.ExecutedSteps.Add($"step-{stepIndex}");
                s.Counter++;
            });
        }
        flow;
    }
}

public class BranchingFlow : FlowConfig<BenchmarkState>
{
    protected override void Configure(IFlowBuilder<BenchmarkState> flow)
    {
        flow.Switch(s => s.BranchSelector)
            .Case(0, b => b)
            .Case(1, b => b)
            .Case(2, b => b)
            .Default(b => b)
            .EndSwitch();
    }
}

public class ParallelFlow : FlowConfig<BenchmarkState>
{
    protected override void Configure(IFlowBuilder<BenchmarkState> flow)
    {
        flow.ForEach(s => s.Items)
            .WithParallelism(10)
            .Configure((item, f) => f))
            .EndForEach();

        flow;
    }
}

public class ComplexFlow : FlowConfig<BenchmarkState>
{
    protected override void Configure(IFlowBuilder<BenchmarkState> flow)
    {
        flow;

        flow.If(s => s.BranchSelector == 0)
            
        .ElseIf(s => s.BranchSelector == 1)
            
        .Else()
            
        .EndIf();

        flow.ForEach(s => s.Items.Take(5))
            .WithParallelism(3)
            .Configure((item, f) => f))
            .EndForEach();

        flow.WhenAll(
            f => f,
            f => f,
            f => f
        );

        flow;
    }
}

public class BenchmarkCompensationFlow : FlowConfig<BenchmarkState>
{
    protected override void Configure(IFlowBuilder<BenchmarkState> flow)
    {
        flow)
            .Compensate(s => s.CompensationSteps.Add("undo-step1"));

        flow)
            .Compensate(s => s.CompensationSteps.Add("undo-step2"));

        flow.Step("step3", s =>
        {
            if (s.ShouldFail)
                throw new Exception("Simulated failure");
            s.ExecutedSteps.Add("step3");
        })
            .Compensate(s => s.CompensationSteps.Add("undo-step3"));

        flow;
    }
}

public class HeavyStateFlow : FlowConfig<BenchmarkState>
{
    protected override void Configure(IFlowBuilder<BenchmarkState> flow)
    {
        flow.Step("process-data", s =>
        {
            s.DataProcessed = true;
            s.ProcessingTime = DateTime.UtcNow;
        });

        flow.Step("transform", s =>
        {
            foreach (var kvp in s.Dictionary)
            {
                s.TransformedData[kvp.Key] = kvp.Value?.ToString() ?? "";
            }
        });

        flow;
    }
}

// Benchmark state
public class BenchmarkState : IFlowState
{
    public string? FlowId { get; set; }
    public int StepCount { get; set; }
    public int Counter { get; set; }
    public int BranchSelector { get; set; }
    public int SelectedBranch { get; set; }
    public bool ShouldFail { get; set; }
    public bool Completed { get; set; }
    public bool DataProcessed { get; set; }
    public DateTime ProcessingTime { get; set; }

    public List<string> Items { get; set; } = new();
    public HashSet<string> ProcessedItems { get; set; } = new();
    public List<string> ExecutedSteps { get; set; } = new();
    public List<string> CompensationSteps { get; set; } = new();
    public Dictionary<int, bool> ParallelCompleted { get; set; } = new() { [0] = false, [1] = false, [2] = false };

    public byte[] LargeData { get; set; } = Array.Empty<byte>();
    public Dictionary<string, object> Dictionary { get; set; } = new();
    public Dictionary<string, string> TransformedData { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
