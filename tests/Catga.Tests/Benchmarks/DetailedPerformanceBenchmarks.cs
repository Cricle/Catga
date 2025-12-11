using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using Catga.Flow.Dsl;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.Benchmarks;

/// <summary>
/// Detailed performance benchmarks for specific scenarios
/// </summary>
[Config(typeof(DetailedBenchmarkConfig))]
[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class DetailedPerformanceBenchmarks
{
    private IServiceProvider? _catgaProvider;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddFlowDsl();

        // Register all test flows
        services.AddFlow<LatencyTestState, LatencyTestFlow>();
        services.AddFlow<ThroughputTestState, ThroughputTestFlow>();
        services.AddFlow<ScalabilityTestState, ScalabilityTestFlow>();
        services.AddFlow<ResilienceTestState, ResilienceTestFlow>();
        services.AddFlow<StateManagementTestState, StateManagementTestFlow>();

        _catgaProvider = services.BuildServiceProvider();
    }

    #region Latency Benchmarks

    [Benchmark]
    [BenchmarkCategory("Latency")]
    public async Task<long> Catga_MinimalLatency()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<LatencyTestState, LatencyTestFlow>>();
        var state = new LatencyTestState { FlowId = Guid.NewGuid().ToString() };

        var sw = Stopwatch.StartNew();
        await executor!.RunAsync(state);
        sw.Stop();

        return sw.ElapsedTicks;
    }

    [Benchmark]
    [BenchmarkCategory("Latency")]
    public async Task<(double p50, double p95, double p99)> Catga_LatencyPercentiles()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<LatencyTestState, LatencyTestFlow>>();
        var latencies = new List<double>(1000);

        for (int i = 0; i < 1000; i++)
        {
            var state = new LatencyTestState { FlowId = $"latency-{i}" };
            var sw = Stopwatch.StartNew();
            await executor!.RunAsync(state);
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        latencies.Sort();
        return (
            p50: latencies[(int)(latencies.Count * 0.50)],
            p95: latencies[(int)(latencies.Count * 0.95)],
            p99: latencies[(int)(latencies.Count * 0.99)]
        );
    }

    #endregion

    #region Throughput Benchmarks

    [Benchmark]
    [BenchmarkCategory("Throughput")]
    [Arguments(100)]
    [Arguments(1000)]
    [Arguments(10000)]
    public async Task<double> Catga_ThroughputPerSecond(int requestCount)
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<ThroughputTestState, ThroughputTestFlow>>();

        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, requestCount).Select(async i =>
        {
            var state = new ThroughputTestState
            {
                FlowId = $"throughput-{i}",
                RequestNumber = i
            };
            await executor!.RunAsync(state);
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        return requestCount / sw.Elapsed.TotalSeconds;
    }

    [Benchmark]
    [BenchmarkCategory("Throughput")]
    public async Task<double> Catga_SustainedThroughput()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<ThroughputTestState, ThroughputTestFlow>>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var counter = 0;

        var sw = Stopwatch.StartNew();

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var state = new ThroughputTestState
                {
                    FlowId = $"sustained-{counter}",
                    RequestNumber = counter
                };
                await executor!.RunAsync(state);
                counter++;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        sw.Stop();
        return counter / sw.Elapsed.TotalSeconds;
    }

    #endregion

    #region Scalability Benchmarks

    [Benchmark]
    [BenchmarkCategory("Scalability")]
    [Arguments(1, 10)]
    [Arguments(10, 100)]
    [Arguments(100, 1000)]
    public async Task<double> Catga_ScalabilityTest(int concurrency, int itemsPerFlow)
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<ScalabilityTestState, ScalabilityTestFlow>>();
        var semaphore = new SemaphoreSlim(concurrency);

        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, concurrency).Select(async flowId =>
        {
            await semaphore.WaitAsync();
            try
            {
                var state = new ScalabilityTestState
                {
                    FlowId = $"scale-{flowId}",
                    Items = Enumerable.Range(0, itemsPerFlow).Select(i => $"item-{i}").ToList()
                };
                await executor!.RunAsync(state);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        var totalItems = concurrency * itemsPerFlow;
        return totalItems / sw.Elapsed.TotalSeconds;
    }

    [Benchmark]
    [BenchmarkCategory("Scalability")]
    public async Task<int> Catga_MaxConcurrentFlows()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<ScalabilityTestState, ScalabilityTestFlow>>();
        var activeFlows = 0;
        var maxConcurrent = 0;
        var tasks = new List<Task>();

        for (int i = 0; i < 1000; i++)
        {
            var flowId = i;
            tasks.Add(Task.Run(async () =>
            {
                Interlocked.Increment(ref activeFlows);
                var current = activeFlows;
                if (current > maxConcurrent)
                {
                    Interlocked.CompareExchange(ref maxConcurrent, current, maxConcurrent);
                }

                var state = new ScalabilityTestState
                {
                    FlowId = $"concurrent-{flowId}",
                    Items = new List<string> { "item1", "item2", "item3" }
                };

                await executor!.RunAsync(state);
                Interlocked.Decrement(ref activeFlows);
            }));

            if (i % 10 == 0) await Task.Delay(1); // Spread out the load
        }

        await Task.WhenAll(tasks);
        return maxConcurrent;
    }

    #endregion

    #region Resilience Benchmarks

    [Benchmark]
    [BenchmarkCategory("Resilience")]
    [Arguments(0.0)]  // No failures
    [Arguments(0.1)]  // 10% failure rate
    [Arguments(0.5)]  // 50% failure rate
    public async Task<(int succeeded, int failed, double avgRecoveryMs)> Catga_ResilienceTest(double failureRate)
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<ResilienceTestState, ResilienceTestFlow>>();
        var succeeded = 0;
        var failed = 0;
        var recoveryTimes = new List<double>();

        for (int i = 0; i < 100; i++)
        {
            var state = new ResilienceTestState
            {
                FlowId = $"resilience-{i}",
                ShouldFail = Random.Shared.NextDouble() < failureRate,
                FailurePoint = Random.Shared.Next(1, 4)
            };

            var sw = Stopwatch.StartNew();
            try
            {
                var result = await executor!.RunAsync(state);
                succeeded++;
            }
            catch
            {
                failed++;
                recoveryTimes.Add(sw.Elapsed.TotalMilliseconds);
            }
        }

        var avgRecovery = recoveryTimes.Any() ? recoveryTimes.Average() : 0;
        return (succeeded, failed, avgRecovery);
    }

    [Benchmark]
    [BenchmarkCategory("Resilience")]
    public async Task<double> Catga_CompensationOverhead()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<ResilienceTestState, ResilienceTestFlow>>();

        // Measure successful flow
        var successState = new ResilienceTestState
        {
            FlowId = "success",
            ShouldFail = false
        };

        var swSuccess = Stopwatch.StartNew();
        await executor!.RunAsync(successState);
        swSuccess.Stop();

        // Measure flow with compensation
        var failState = new ResilienceTestState
        {
            FlowId = "fail",
            ShouldFail = true,
            FailurePoint = 3
        };

        var swFail = Stopwatch.StartNew();
        try
        {
            await executor.RunAsync(failState);
        }
        catch
        {
            // Expected
        }
        swFail.Stop();

        return (swFail.Elapsed.TotalMilliseconds - swSuccess.Elapsed.TotalMilliseconds) / swSuccess.Elapsed.TotalMilliseconds * 100;
    }

    #endregion

    #region State Management Benchmarks

    [Benchmark]
    [BenchmarkCategory("StateManagement")]
    [Arguments(100)]    // 100 bytes
    [Arguments(1024)]   // 1 KB
    [Arguments(10240)]  // 10 KB
    [Arguments(102400)] // 100 KB
    public async Task<double> Catga_StateTransferOverhead(int stateSize)
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<StateManagementTestState, StateManagementTestFlow>>();

        var state = new StateManagementTestState
        {
            FlowId = $"state-{stateSize}",
            Data = new byte[stateSize],
            Metadata = Enumerable.Range(0, stateSize / 100)
                .ToDictionary(i => $"key-{i}", i => (object)$"value-{i}")
        };

        Random.Shared.NextBytes(state.Data);

        var sw = Stopwatch.StartNew();
        await executor!.RunAsync(state);
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds;
    }

    [Benchmark]
    [BenchmarkCategory("StateManagement")]
    public async Task<long> Catga_StateSerializationCost()
    {
        var store = _catgaProvider!.GetService<IDslFlowStore>();

        var state = new StateManagementTestState
        {
            FlowId = Guid.NewGuid().ToString(),
            Data = new byte[10240],
            Metadata = Enumerable.Range(0, 100)
                .ToDictionary(i => $"key-{i}", i => (object)new
                {
                    Id = i,
                    Value = $"value-{i}",
                    Timestamp = DateTime.UtcNow,
                    Nested = new { A = i, B = i * 2, C = i * 3 }
                })
        };

        var snapshot = new FlowSnapshot<StateManagementTestState>
        {
            FlowId = state.FlowId,
            State = state,
            Status = DslFlowStatus.Running,
            Position = new FlowPosition(new[] { 0, 1, 2, 3, 4 }),
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var sw = Stopwatch.StartNew();
        await store!.CreateAsync(snapshot);
        var retrieved = await store.GetAsync<StateManagementTestState>(state.FlowId!);
        await store.DeleteAsync(state.FlowId!);
        sw.Stop();

        return sw.ElapsedTicks;
    }

    #endregion

    #region Memory Benchmarks

    [Benchmark]
    [BenchmarkCategory("Memory")]
    public async Task<long> Catga_MemoryAllocationPerFlow()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<LatencyTestState, LatencyTestFlow>>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalMemory(false);

        for (int i = 0; i < 100; i++)
        {
            var state = new LatencyTestState { FlowId = $"memory-{i}" };
            await executor!.RunAsync(state);
        }

        var after = GC.GetTotalMemory(false);
        return (after - before) / 100;
    }

    [Benchmark]
    [BenchmarkCategory("Memory")]
    public async Task<(int gen0, int gen1, int gen2)> Catga_GCPressure()
    {
        var executor = _catgaProvider!.GetService<DslFlowExecutor<LatencyTestState, LatencyTestFlow>>();

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);

        for (int i = 0; i < 1000; i++)
        {
            var state = new LatencyTestState { FlowId = $"gc-{i}" };
            await executor!.RunAsync(state);
        }

        return (
            gen0: GC.CollectionCount(0) - gen0Before,
            gen1: GC.CollectionCount(1) - gen1Before,
            gen2: GC.CollectionCount(2) - gen2Before
        );
    }

    #endregion
}

public class DetailedBenchmarkConfig : ManualConfig
{
    public DetailedBenchmarkConfig()
    {
        AddColumn(TargetMethodColumn.Method);
        AddColumn(new ParamColumn("Args"));
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.P90);
        AddColumn(StatisticColumn.P95);
        AddColumn(StatisticColumn.P99);
        AddColumn(StatisticColumn.Min);
        AddColumn(StatisticColumn.Max);

        AddDiagnoser(MemoryDiagnoser.Default);
        AddDiagnoser(ThreadingDiagnoser.Default);

        AddExporter(HtmlExporter.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(CsvExporter.Default);

        AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory);
    }
}

// Test flows
public class LatencyTestFlow : FlowConfig<LatencyTestState>
{
    protected override void Configure(IFlowBuilder<LatencyTestState> flow)
    {
        flow;
    }
}

public class ThroughputTestFlow : FlowConfig<ThroughputTestState>
{
    protected override void Configure(IFlowBuilder<ThroughputTestState> flow)
    {
        flow.Step("process", s =>
        {
            s.ProcessedAt = DateTime.UtcNow;
            s.Result = s.RequestNumber * 2;
        });
    }
}

public class ScalabilityTestFlow : FlowConfig<ScalabilityTestState>
{
    protected override void Configure(IFlowBuilder<ScalabilityTestState> flow)
    {
        flow.ForEach(s => s.Items)
            .WithParallelism(10)
            .Configure((item, f) => f))
            .EndForEach();
    }
}

public class ResilienceTestFlow : FlowConfig<ResilienceTestState>
{
    protected override void Configure(IFlowBuilder<ResilienceTestState> flow)
    {
        flow)
            .Compensate(s => s.CompensatedSteps.Add(1));

        flow.Step("step2", s =>
        {
            if (s.ShouldFail && s.FailurePoint == 2)
                throw new Exception("Simulated failure at step 2");
            s.CompletedSteps.Add(2);
        })
            .Compensate(s => s.CompensatedSteps.Add(2));

        flow.Step("step3", s =>
        {
            if (s.ShouldFail && s.FailurePoint == 3)
                throw new Exception("Simulated failure at step 3");
            s.CompletedSteps.Add(3);
        })
            .Compensate(s => s.CompensatedSteps.Add(3));
    }
}

public class StateManagementTestFlow : FlowConfig<StateManagementTestState>
{
    protected override void Configure(IFlowBuilder<StateManagementTestState> flow)
    {
        flow.Step("process", s =>
        {
            s.ProcessedDataSize = s.Data.Length;
            s.MetadataCount = s.Metadata.Count;
        });

        flow.Step("transform", s =>
        {
            foreach (var key in s.Metadata.Keys.ToList())
            {
                s.Metadata[key] = s.Metadata[key]?.ToString()?.ToUpper() ?? "";
            }
        });
    }
}

// Test states
public class LatencyTestState : IFlowState
{
    public string? FlowId { get; set; }
    public bool Executed { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ThroughputTestState : IFlowState
{
    public string? FlowId { get; set; }
    public int RequestNumber { get; set; }
    public DateTime ProcessedAt { get; set; }
    public int Result { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ScalabilityTestState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = new();
    public HashSet<string> ProcessedItems { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ResilienceTestState : IFlowState
{
    public string? FlowId { get; set; }
    public bool ShouldFail { get; set; }
    public int FailurePoint { get; set; }
    public List<int> CompletedSteps { get; set; } = new();
    public List<int> CompensatedSteps { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class StateManagementTestState : IFlowState
{
    public string? FlowId { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public int ProcessedDataSize { get; set; }
    public int MetadataCount { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
