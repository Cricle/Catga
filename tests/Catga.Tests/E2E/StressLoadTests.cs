using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Stress and load E2E tests.
/// Tests system behavior under high load, concurrent requests, and resource exhaustion.
/// </summary>
public class StressLoadTests
{
    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Stress_HighConcurrency_HandlesAllRequests()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var completedCount = 0;
        var errorCount = 0;

        var flow = FlowBuilder.Create<StressState>("high-concurrency")
            .Step("process", async (state, ct) =>
            {
                await Task.Delay(Random.Shared.Next(1, 10), ct);
                state.Processed = true;
                return true;
            })
            .Build();

        var tasks = Enumerable.Range(1, 1000).Select(async i =>
        {
            try
            {
                var state = new StressState { FlowId = $"stress-{i}" };
                var result = await executor.ExecuteAsync(flow, state);
                if (result.IsSuccess) Interlocked.Increment(ref completedCount);
                else Interlocked.Increment(ref errorCount);
            }
            catch
            {
                Interlocked.Increment(ref errorCount);
            }
        });

        await Task.WhenAll(tasks);

        completedCount.Should().Be(1000);
        errorCount.Should().Be(0);
    }

    [Fact]
    public async Task Load_SustainedThroughput_MaintainsPerformance()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var latencies = new ConcurrentBag<long>();

        var flow = FlowBuilder.Create<LoadState>("sustained-load")
            .Step("quick-operation", async (state, ct) =>
            {
                state.Value = state.Input * 2;
                return true;
            })
            .Build();

        var sw = Stopwatch.StartNew();
        var tasks = new List<Task>();

        // Sustained load for 500 requests
        for (int i = 0; i < 500; i++)
        {
            var taskSw = Stopwatch.StartNew();
            var state = new LoadState { FlowId = $"load-{i}", Input = i };

            tasks.Add(executor.ExecuteAsync(flow, state).AsTask().ContinueWith(t =>
            {
                taskSw.Stop();
                latencies.Add(taskSw.ElapsedMilliseconds);
            }));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        var avgLatency = latencies.Average();
        var p95Latency = latencies.OrderBy(l => l).ElementAt((int)(latencies.Count * 0.95));

        avgLatency.Should().BeLessThan(100); // Average under 100ms
        p95Latency.Should().BeLessThan(200); // P95 under 200ms
    }

    [Fact]
    public async Task Stress_BurstTraffic_HandlesSpikes()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var results = new ConcurrentBag<bool>();

        var flow = FlowBuilder.Create<StressState>("burst-traffic")
            .Step("handle-burst", async (state, ct) =>
            {
                state.Processed = true;
                return true;
            })
            .Build();

        // Simulate burst: 200 requests in rapid succession
        var burstTasks = Enumerable.Range(1, 200).Select(async i =>
        {
            var state = new StressState { FlowId = $"burst-{i}" };
            var result = await executor.ExecuteAsync(flow, state);
            results.Add(result.IsSuccess);
        });

        await Task.WhenAll(burstTasks);

        results.Count(r => r).Should().Be(200);
    }

    [Fact]
    public async Task Load_MemoryPressure_DoesNotLeak()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<MemoryState>("memory-test")
            .Step("allocate", async (state, ct) =>
            {
                state.Data = new byte[1024]; // 1KB allocation per request
                return true;
            })
            .Build();

        var initialMemory = GC.GetTotalMemory(true);

        // Execute 1000 flows
        for (int i = 0; i < 1000; i++)
        {
            var state = new MemoryState { FlowId = $"mem-{i}" };
            await executor.ExecuteAsync(flow, state);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var finalMemory = GC.GetTotalMemory(true);
        var memoryGrowth = finalMemory - initialMemory;

        // Memory growth should be reasonable (less than 50MB)
        memoryGrowth.Should().BeLessThan(50 * 1024 * 1024);
    }

    [Fact]
    public async Task Stress_LongRunningWithConcurrency_Completes()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var completionTimes = new ConcurrentBag<long>();

        var flow = FlowBuilder.Create<StressState>("long-running-concurrent")
            .Step("step-1", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return true;
            })
            .Step("step-2", async (state, ct) =>
            {
                await Task.Delay(10, ct);
                return true;
            })
            .Step("step-3", async (state, ct) =>
            {
                state.Processed = true;
                return true;
            })
            .Build();

        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(1, 100).Select(async i =>
        {
            var taskSw = Stopwatch.StartNew();
            var state = new StressState { FlowId = $"lr-{i}" };
            await executor.ExecuteAsync(flow, state);
            taskSw.Stop();
            completionTimes.Add(taskSw.ElapsedMilliseconds);
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        // With 100 concurrent flows of ~20ms each, total should be much less than 100*20ms
        sw.ElapsedMilliseconds.Should().BeLessThan(5000);
    }

    [Fact]
    public async Task Load_GradualRampUp_MaintainsStability()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var phaseThroughputs = new List<double>();

        var flow = FlowBuilder.Create<LoadState>("ramp-up")
            .Step("process", async (state, ct) =>
            {
                state.Value = state.Input + 1;
                return true;
            })
            .Build();

        // Ramp up in phases: 10, 50, 100, 200 concurrent
        var phases = new[] { 10, 50, 100, 200 };

        foreach (var concurrency in phases)
        {
            var phaseSw = Stopwatch.StartNew();
            var tasks = Enumerable.Range(1, concurrency).Select(async i =>
            {
                var state = new LoadState { FlowId = $"ramp-{concurrency}-{i}", Input = i };
                await executor.ExecuteAsync(flow, state);
            });

            await Task.WhenAll(tasks);
            phaseSw.Stop();

            var throughput = concurrency / (phaseSw.ElapsedMilliseconds / 1000.0);
            phaseThroughputs.Add(throughput);
        }

        // Throughput should not degrade significantly
        var minThroughput = phaseThroughputs.Min();
        var maxThroughput = phaseThroughputs.Max();

        // Throughput variance should be within reasonable bounds
        (maxThroughput / minThroughput).Should().BeLessThan(10);
    }

    [Fact]
    public async Task Stress_ErrorRecovery_ContinuesAfterFailures()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var successCount = 0;
        var failCount = 0;

        var flow = FlowBuilder.Create<ErrorStressState>("error-recovery")
            .Step("maybe-fail", async (state, ct) =>
            {
                if (state.ShouldFail)
                {
                    throw new InvalidOperationException("Simulated failure");
                }
                state.Processed = true;
                return true;
            })
            .Build();

        // Mix of successful and failing requests
        var tasks = Enumerable.Range(1, 200).Select(async i =>
        {
            var state = new ErrorStressState
            {
                FlowId = $"err-{i}",
                ShouldFail = i % 10 == 0 // 10% failure rate
            };

            var result = await executor.ExecuteAsync(flow, state);

            if (result.IsSuccess) Interlocked.Increment(ref successCount);
            else Interlocked.Increment(ref failCount);
        });

        await Task.WhenAll(tasks);

        successCount.Should().Be(180); // 90% success
        failCount.Should().Be(20); // 10% failure
    }

    [Fact]
    public async Task Load_ParallelFlows_ScalesLinearly()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<LoadState>("parallel-scale")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process", async (state, ct) =>
                {
                    await Task.Delay(5, ct);
                    state.ProcessedItems.Add(item);
                    return true;
                }))
            .WithParallelism(10)
            .Build();

        var state = new LoadState
        {
            FlowId = "parallel-test",
            Items = Enumerable.Range(1, 50).Select(i => $"item-{i}").ToList()
        };

        var sw = Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(flow, state);
        sw.Stop();

        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(50);

        // With parallelism 10 and 5ms per item, 50 items should take ~25ms (5 batches)
        // Allow some overhead
        sw.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    [Fact]
    public async Task Stress_ResourceContention_HandlesGracefully()
    {
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var sharedResource = new SemaphoreSlim(5); // Only 5 concurrent access
        var accessedCount = 0;
        var waitedCount = 0;

        var flow = FlowBuilder.Create<StressState>("resource-contention")
            .Step("access-resource", async (state, ct) =>
            {
                if (await sharedResource.WaitAsync(TimeSpan.FromMilliseconds(100), ct))
                {
                    try
                    {
                        Interlocked.Increment(ref accessedCount);
                        await Task.Delay(20, ct);
                    }
                    finally
                    {
                        sharedResource.Release();
                    }
                }
                else
                {
                    Interlocked.Increment(ref waitedCount);
                }

                state.Processed = true;
                return true;
            })
            .Build();

        var tasks = Enumerable.Range(1, 50).Select(async i =>
        {
            var state = new StressState { FlowId = $"contention-{i}" };
            await executor.ExecuteAsync(flow, state);
        });

        await Task.WhenAll(tasks);

        // Most should have accessed, some may have waited
        accessedCount.Should().BeGreaterThan(0);
    }

    #region State Classes

    public class StressState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool Processed { get; set; }
    }

    public class LoadState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int Input { get; set; }
        public int Value { get; set; }
        public List<string> Items { get; set; } = new();
        public List<string> ProcessedItems { get; set; } = new();
    }

    public class MemoryState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public byte[]? Data { get; set; }
    }

    public class ErrorStressState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public bool ShouldFail { get; set; }
        public bool Processed { get; set; }
    }

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
