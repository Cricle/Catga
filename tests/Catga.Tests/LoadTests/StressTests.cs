using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.LoadTests;

/// <summary>
/// Stress tests to find the breaking point of the system
/// </summary>
public class StressTests
{
    private readonly ITestOutputHelper _output;

    public StressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task StressTest_MaxConcurrentFlows()
    {
        // Find the maximum number of concurrent flows the system can handle
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<StressTestState, StressTestFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<StressTestState, StressTestFlow>>();

        var concurrentFlows = 0;
        var maxConcurrent = 0;
        var completed = 0;
        var failed = 0;

        var tasks = new List<Task>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _output.WriteLine("Starting stress test - Maximum concurrent flows...");

        for (int i = 0; i < 10000; i++)
        {
            if (cts.Token.IsCancellationRequested) break;

            var flowId = i;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    Interlocked.Increment(ref concurrentFlows);
                    var current = concurrentFlows;

                    // Track max concurrent
                    while (current > maxConcurrent)
                    {
                        Interlocked.CompareExchange(ref maxConcurrent, current, maxConcurrent);
                        current = concurrentFlows;
                    }

                    var state = new StressTestState
                    {
                        FlowId = $"stress-{flowId}",
                        StartTime = DateTime.UtcNow
                    };

                    await executor!.RunAsync(state);
                    Interlocked.Increment(ref completed);
                }
                catch
                {
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    Interlocked.Decrement(ref concurrentFlows);
                }
            }));

            // Gradually increase load
            if (i % 100 == 0)
            {
                await Task.Delay(10);
                _output.WriteLine($"Progress: {i} flows started, {maxConcurrent} max concurrent, {completed} completed, {failed} failed");
            }
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Some tasks might fail under stress
        }

        _output.WriteLine($"=== Stress Test Results ===");
        _output.WriteLine($"Maximum Concurrent Flows: {maxConcurrent}");
        _output.WriteLine($"Total Completed: {completed}");
        _output.WriteLine($"Total Failed: {failed}");
        _output.WriteLine($"Success Rate: {(completed * 100.0 / (completed + failed)):F2}%");

        maxConcurrent.Should().BeGreaterThan(100, "should handle at least 100 concurrent flows");
        var successRate = completed * 100.0 / (completed + failed);
        successRate.Should().BeGreaterThan(95, "should maintain >95% success rate under stress");
    }

    [Fact]
    public async Task StressTest_SustainedLoad()
    {
        // Test sustained load over extended period
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<StressTestState, StressTestFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<StressTestState, StressTestFlow>>();

        const int targetTps = 1000; // Target transactions per second
        const int duration = 60; // seconds
        const int interval = 100; // ms
        const int flowsPerInterval = targetTps * interval / 1000;

        var completed = 0;
        var failed = 0;
        var latencies = new List<double>();
        var sw = Stopwatch.StartNew();

        _output.WriteLine($"Starting sustained load test - {targetTps} TPS for {duration} seconds...");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(duration));

        while (!cts.Token.IsCancellationRequested)
        {
            var intervalTasks = new List<Task>();

            for (int i = 0; i < flowsPerInterval; i++)
            {
                intervalTasks.Add(Task.Run(async () =>
                {
                    var flowSw = Stopwatch.StartNew();
                    try
                    {
                        var state = new StressTestState
                        {
                            FlowId = $"sustained-{Guid.NewGuid()}",
                            StartTime = DateTime.UtcNow
                        };

                        await executor!.RunAsync(state);
                        Interlocked.Increment(ref completed);

                        lock (latencies)
                        {
                            latencies.Add(flowSw.Elapsed.TotalMilliseconds);
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref failed);
                    }
                }));
            }

            await Task.WhenAll(intervalTasks);
            await Task.Delay(interval);

            if (sw.ElapsedMilliseconds % 5000 < interval)
            {
                var currentTps = completed / Math.Max(1, sw.Elapsed.TotalSeconds);
                _output.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F0}s, Completed: {completed}, Failed: {failed}, TPS: {currentTps:F0}");
            }
        }

        sw.Stop();

        // Calculate statistics
        latencies.Sort();
        var p50 = latencies.Any() ? latencies[(int)(latencies.Count * 0.50)] : 0;
        var p95 = latencies.Any() ? latencies[(int)(latencies.Count * 0.95)] : 0;
        var p99 = latencies.Any() ? latencies[(int)(latencies.Count * 0.99)] : 0;
        var actualTps = completed / sw.Elapsed.TotalSeconds;

        _output.WriteLine($"=== Sustained Load Test Results ===");
        _output.WriteLine($"Duration: {sw.Elapsed.TotalSeconds:F1}s");
        _output.WriteLine($"Completed: {completed}");
        _output.WriteLine($"Failed: {failed}");
        _output.WriteLine($"Success Rate: {(completed * 100.0 / Math.Max(1, completed + failed)):F2}%");
        _output.WriteLine($"Actual TPS: {actualTps:F0} (Target: {targetTps})");
        _output.WriteLine($"Latency P50: {p50:F2}ms");
        _output.WriteLine($"Latency P95: {p95:F2}ms");
        _output.WriteLine($"Latency P99: {p99:F2}ms");

        actualTps.Should().BeGreaterThan(targetTps * 0.9, $"should maintain at least 90% of target TPS ({targetTps})");
        p99.Should().BeLessThan(100, "P99 latency should be under 100ms");
    }

    [Fact]
    public async Task StressTest_MemoryPressure()
    {
        // Test behavior under memory pressure with large states
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<LargeStateFlow, LargeStateFlowConfig>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<LargeStateFlow, LargeStateFlowConfig>>();

        const int flowCount = 1000;
        const int stateSize = 100 * 1024; // 100KB per state

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var startMemory = GC.GetTotalMemory(true);
        var completed = 0;
        var failed = 0;

        _output.WriteLine($"Starting memory pressure test - {flowCount} flows with {stateSize / 1024}KB states...");

        var tasks = Enumerable.Range(0, flowCount).Select(i => Task.Run(async () =>
        {
            try
            {
                var state = new LargeStateFlow
                {
                    FlowId = $"memory-{i}",
                    LargeData = new byte[stateSize],
                    Metadata = Enumerable.Range(0, 1000)
                        .ToDictionary(j => $"key-{j}", j => (object)$"value-{j}")
                };

                Random.Shared.NextBytes(state.LargeData);

                await executor!.RunAsync(state);
                Interlocked.Increment(ref completed);
            }
            catch
            {
                Interlocked.Increment(ref failed);
            }
        }));

        await Task.WhenAll(tasks);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var endMemory = GC.GetTotalMemory(true);
        var memoryUsed = endMemory - startMemory;
        var memoryPerFlow = memoryUsed / flowCount;

        _output.WriteLine($"=== Memory Pressure Test Results ===");
        _output.WriteLine($"Flows Processed: {flowCount}");
        _output.WriteLine($"State Size: {stateSize / 1024}KB");
        _output.WriteLine($"Completed: {completed}");
        _output.WriteLine($"Failed: {failed}");
        _output.WriteLine($"Total Memory Used: {memoryUsed / (1024 * 1024)}MB");
        _output.WriteLine($"Memory Per Flow: {memoryPerFlow / 1024}KB");
        _output.WriteLine($"GC Collections - Gen0: {GC.CollectionCount(0)}, Gen1: {GC.CollectionCount(1)}, Gen2: {GC.CollectionCount(2)}");

        completed.Should().Be(flowCount, "all flows should complete even under memory pressure");
        memoryPerFlow.Should().BeLessThan(stateSize * 2, "memory overhead should be less than 2x state size");
    }

    [Fact]
    public async Task StressTest_RapidStartStop()
    {
        // Test rapid creation and disposal of flows
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<StressTestState, StressTestFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<StressTestState, StressTestFlow>>();

        const int iterations = 10000;
        var completed = 0;
        var failed = 0;

        _output.WriteLine($"Starting rapid start/stop test - {iterations} iterations...");

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            try
            {
                var state = new StressTestState
                {
                    FlowId = $"rapid-{i}",
                    StartTime = DateTime.UtcNow
                };

                // Start and immediately cancel
                using var cts = new CancellationTokenSource();
                var task = executor!.RunAsync(state).AsTask();

                if (i % 2 == 0)
                {
                    cts.Cancel(); // Cancel half of them
                }

                await task;
                completed++;
            }
            catch (OperationCanceledException)
            {
                completed++; // Cancellation is expected
            }
            catch
            {
                failed++;
            }
        }

        sw.Stop();

        var throughput = iterations / sw.Elapsed.TotalSeconds;

        _output.WriteLine($"=== Rapid Start/Stop Test Results ===");
        _output.WriteLine($"Iterations: {iterations}");
        _output.WriteLine($"Duration: {sw.Elapsed.TotalSeconds:F2}s");
        _output.WriteLine($"Throughput: {throughput:F0} ops/sec");
        _output.WriteLine($"Completed: {completed}");
        _output.WriteLine($"Failed: {failed}");

        failed.Should().Be(0, "no failures during rapid start/stop");
        throughput.Should().BeGreaterThan(1000, "should handle >1000 start/stop operations per second");
    }

    [Fact]
    public async Task StressTest_ComplexFlowUnderLoad()
    {
        // Test complex flows with all features under load
        var services = new ServiceCollection();
        services.AddFlowDsl();
        services.AddFlow<ComplexStressState, ComplexStressFlow>();
        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ComplexStressState, ComplexStressFlow>>();

        const int flowCount = 100;
        const int itemsPerFlow = 100;

        var completed = 0;
        var failed = 0;
        var totalDuration = TimeSpan.Zero;

        _output.WriteLine($"Starting complex flow stress test - {flowCount} flows with {itemsPerFlow} items each...");

        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, flowCount).Select(flowId => Task.Run(async () =>
        {
            var flowSw = Stopwatch.StartNew();
            try
            {
                var state = new ComplexStressState
                {
                    FlowId = $"complex-{flowId}",
                    Items = Enumerable.Range(0, itemsPerFlow).Select(i => $"item-{i}").ToList(),
                    BranchSelector = flowId % 3,
                    ShouldCompensate = flowId % 10 == 0
                };

                await executor!.RunAsync(state);

                Interlocked.Increment(ref completed);
                Interlocked.Add(ref totalDuration, (long)flowSw.Elapsed.TotalMilliseconds);
            }
            catch
            {
                Interlocked.Increment(ref failed);
            }
        }));

        await Task.WhenAll(tasks);
        sw.Stop();

        var avgDuration = totalDuration.TotalMilliseconds / Math.Max(1, completed);
        var throughput = (flowCount * itemsPerFlow) / sw.Elapsed.TotalSeconds;

        _output.WriteLine($"=== Complex Flow Stress Test Results ===");
        _output.WriteLine($"Total Flows: {flowCount}");
        _output.WriteLine($"Items Per Flow: {itemsPerFlow}");
        _output.WriteLine($"Completed: {completed}");
        _output.WriteLine($"Failed: {failed}");
        _output.WriteLine($"Total Duration: {sw.Elapsed.TotalSeconds:F2}s");
        _output.WriteLine($"Average Flow Duration: {avgDuration:F2}ms");
        _output.WriteLine($"Item Throughput: {throughput:F0} items/sec");

        completed.Should().BeGreaterThan(flowCount * 0.95, "at least 95% of complex flows should complete");
        avgDuration.Should().BeLessThan(1000, "average complex flow should complete within 1 second");
    }
}

// Stress test flows and states
public class StressTestFlow : FlowConfig<StressTestState>
{
    protected override void Configure(IFlowBuilder<StressTestState> flow)
    {
        flow.Step("process", s =>
        {
            s.ProcessedAt = DateTime.UtcNow;
            s.Duration = s.ProcessedAt - s.StartTime;
        });
    }
}

public class StressTestState : IFlowState
{
    public string? FlowId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime ProcessedAt { get; set; }
    public TimeSpan Duration { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class LargeStateFlowConfig : FlowConfig<LargeStateFlow>
{
    protected override void Configure(IFlowBuilder<LargeStateFlow> flow)
    {
        flow.Step("process", s =>
        {
            s.DataProcessed = true;
            s.ProcessedSize = s.LargeData.Length;
        });
    }
}

public class LargeStateFlow : IFlowState
{
    public string? FlowId { get; set; }
    public byte[] LargeData { get; set; } = Array.Empty<byte>();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public bool DataProcessed { get; set; }
    public int ProcessedSize { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ComplexStressFlow : FlowConfig<ComplexStressState>
{
    protected override void Configure(IFlowBuilder<ComplexStressState> flow)
    {
        // Branching
        flow.Switch(s => s.BranchSelector)
            .Case(0, b => b)
            .Case(1, b => b)
            .Case(2, b => b)
            .Default(b => b)
            .EndSwitch();

        // Parallel ForEach
        flow.ForEach(s => s.Items)
            .WithParallelism(10)
            .Configure((item, f) => f))
            .EndForEach();

        // Compensation
        flow.Step("risky", s =>
        {
            if (s.ShouldCompensate)
                throw new Exception("Triggering compensation");
            s.RiskyStepCompleted = true;
        })
            .Compensate(s => s.CompensationExecuted = true);

        // WhenAll coordination
        flow.WhenAll(
            f => f),
            f => f),
            f => f)
        );

        flow;
    }
}

public class ComplexStressState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = new();
    public HashSet<string> ProcessedItems { get; set; } = new();
    public int BranchSelector { get; set; }
    public string BranchExecuted { get; set; } = string.Empty;
    public bool ShouldCompensate { get; set; }
    public bool RiskyStepCompleted { get; set; }
    public bool CompensationExecuted { get; set; }
    public List<string> ParallelSteps { get; set; } = new();
    public bool Completed { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
