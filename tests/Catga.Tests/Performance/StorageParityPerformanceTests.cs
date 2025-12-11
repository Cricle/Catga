using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Performance;

/// <summary>
/// Performance tests comparing the three storage implementations.
/// </summary>
public class StorageParityPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public StorageParityPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Performance_CreateOperation_Comparison()
    {
        // Test create operation performance
        var stores = new Dictionary<string, IDslFlowStore>
        {
            ["InMemory"] = new InMemoryDslFlowStore(),
            // Add mocked Redis and NATS for unit test
        };

        const int iterations = 1000;
        var results = new Dictionary<string, double>();

        foreach (var (name, store) in stores)
        {
            // Warm up
            for (int i = 0; i < 10; i++)
            {
                var warmupSnapshot = CreateSnapshot($"warmup-{i}");
                await store.CreateAsync(warmupSnapshot);
            }

            // Measure
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var snapshot = CreateSnapshot($"perf-create-{name}-{i}");
                await store.CreateAsync(snapshot);
            }
            stopwatch.Stop();

            results[name] = stopwatch.Elapsed.TotalMilliseconds / iterations;
            _output.WriteLine($"{name} Create: {results[name]:F3}ms per operation");
        }

        // InMemory should be fastest
        results["InMemory"].Should().BeLessThan(0.1);
    }

    [Fact]
    public async Task Performance_UpdateWithOptimisticLocking()
    {
        var store = new InMemoryDslFlowStore();
        var flowId = "optimistic-lock-perf";
        var snapshot = CreateSnapshot(flowId);
        await store.CreateAsync(snapshot);

        const int concurrentUpdates = 100;
        var successCount = 0;
        var conflictCount = 0;
        var totalRetries = 0;

        var stopwatch = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, concurrentUpdates).Select(i => Task.Run(async () =>
        {
            var retries = 0;
            const int maxRetries = 10;

            while (retries < maxRetries)
            {
                var current = await store.GetAsync<PerfTestState>(flowId);
                if (current != null)
                {
                    current.State.Counter++;
                    var result = await store.UpdateAsync(current);

                    if (result.IsSuccess)
                    {
                        Interlocked.Increment(ref successCount);
                        Interlocked.Add(ref totalRetries, retries);
                        return true;
                    }
                }

                retries++;
                Interlocked.Increment(ref conflictCount);
                await Task.Delay(Random.Shared.Next(0, 5));
            }

            return false;
        }));

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        _output.WriteLine($"Optimistic Locking Performance:");
        _output.WriteLine($"  Total Updates: {concurrentUpdates}");
        _output.WriteLine($"  Successful: {successCount}");
        _output.WriteLine($"  Conflicts: {conflictCount}");
        _output.WriteLine($"  Average Retries: {(double)totalRetries / successCount:F2}");
        _output.WriteLine($"  Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Throughput: {successCount * 1000.0 / stopwatch.ElapsedMilliseconds:F0} updates/sec");

        successCount.Should().Be(concurrentUpdates);
    }

    [Fact]
    public async Task Performance_ForEachProgress_LargeScale()
    {
        var store = new InMemoryDslFlowStore();
        var flowId = "foreach-perf";
        const int itemCount = 10000;
        const int batchSize = 100;

        var progress = new ForEachProgress
        {
            FlowId = flowId,
            StepIndex = 1,
            ProcessedItems = new HashSet<string>(),
            FailedItems = new List<string>(),
            ItemResults = new Dictionary<string, object?>(),
            TotalBatches = itemCount / batchSize
        };

        var stopwatch = Stopwatch.StartNew();

        // Simulate batch processing
        for (int batch = 0; batch < itemCount / batchSize; batch++)
        {
            // Process batch
            for (int i = 0; i < batchSize; i++)
            {
                var itemId = $"item-{batch * batchSize + i}";
                progress.ProcessedItems.Add(itemId);
                progress.ItemResults[itemId] = $"result-{i}";
            }

            progress.CurrentBatchIndex = batch;

            // Save progress
            await store.SaveForEachProgressAsync(flowId, 1, progress);
        }

        stopwatch.Stop();

        // Verify final state
        var finalProgress = await store.GetForEachProgressAsync(flowId, 1);
        finalProgress!.ProcessedItems.Should().HaveCount(itemCount);
        finalProgress.ItemResults.Should().HaveCount(itemCount);

        _output.WriteLine($"ForEach Progress Performance:");
        _output.WriteLine($"  Items Processed: {itemCount}");
        _output.WriteLine($"  Batches: {itemCount / batchSize}");
        _output.WriteLine($"  Total Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Items/sec: {itemCount * 1000.0 / stopwatch.ElapsedMilliseconds:F0}");
        _output.WriteLine($"  Time per batch: {stopwatch.ElapsedMilliseconds / (double)(itemCount / batchSize):F2}ms");
    }

    [Fact]
    public async Task Performance_WaitCondition_ManySignals()
    {
        var store = new InMemoryDslFlowStore();
        var flowId = "wait-perf";
        const int signalCount = 1000;

        var signals = Enumerable.Range(0, signalCount)
            .Select(i => $"signal-{i}")
            .ToArray();

        // Set wait condition (compat shim)
        await store.SetWaitConditionAsync(flowId, WaitConditionType.WhenAll, signals,
            DateTime.UtcNow.AddMinutes(10));

        var stopwatch = Stopwatch.StartNew();

        // Complete all signals
        for (int i = 0; i < signalCount; i++)
        {
            var condition = await store.UpdateWaitConditionAsync(flowId, signals[i], $"result-{i}");

            if (i == signalCount - 1)
            {
                condition.Should().NotBeNull();
                condition!.CompletedCount.Should().Be(signalCount);
            }
        }

        stopwatch.Stop();

        var finalCondition = await store.GetWaitConditionAsync(flowId);
        finalCondition.Should().NotBeNull();
        finalCondition!.CompletedCount.Should().Be(signalCount);
        finalCondition.Results.Should().HaveCount(signalCount);

        _output.WriteLine($"Wait Condition Performance:");
        _output.WriteLine($"  Signal Count: {signalCount}");
        _output.WriteLine($"  Total Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Signals/sec: {signalCount * 1000.0 / stopwatch.ElapsedMilliseconds:F0}");
        _output.WriteLine($"  Time per signal: {stopwatch.ElapsedMilliseconds / (double)signalCount:F3}ms");
    }

    [Fact]
    public async Task Performance_TimeoutScanning_LargeDataset()
    {
        var store = new InMemoryDslFlowStore();
        const int conditionCount = 10000;
        const int expiredPercentage = 10;

        var now = DateTime.UtcNow;
        var createdConditions = new List<string>();

        // Create many wait conditions with various timeouts
        for (int i = 0; i < conditionCount; i++)
        {
            var flowId = $"timeout-scan-{i}";
            createdConditions.Add(flowId);

            var timeout = i % (100 / expiredPercentage) == 0
                ? now.AddSeconds(-10) // Expired
                : now.AddMinutes(10); // Future

            await store.SetWaitConditionAsync(flowId, WaitConditionType.WhenAll,
                new[] { "signal" }, timeout);
        }

        // Measure timeout scanning
        var stopwatch = Stopwatch.StartNew();
        var timedOut = await store.GetTimedOutWaitConditionsAsync();
        stopwatch.Stop();

        var expectedExpired = conditionCount * expiredPercentage / 100;
        timedOut.Should().HaveCount(expectedExpired);

        _output.WriteLine($"Timeout Scanning Performance:");
        _output.WriteLine($"  Total Conditions: {conditionCount}");
        _output.WriteLine($"  Expired Found: {timedOut.Count}");
        _output.WriteLine($"  Scan Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Throughput: {conditionCount * 1000.0 / stopwatch.ElapsedMilliseconds:F0} conditions/sec");
    }

    [Fact]
    public async Task Performance_ConcurrentMixedOperations()
    {
        var store = new InMemoryDslFlowStore();
        const int flowCount = 100;
        const int operationsPerFlow = 10;

        var flows = Enumerable.Range(0, flowCount)
            .Select(i => $"mixed-ops-{i}")
            .ToList();

        // Create initial flows
        foreach (var flowId in flows)
        {
            var snapshot = CreateSnapshot(flowId);
            await store.CreateAsync(snapshot);
        }

        var createCount = 0;
        var updateCount = 0;
        var deleteCount = 0;
        var getCount = 0;

        var stopwatch = Stopwatch.StartNew();

        // Perform mixed operations concurrently
        var tasks = flows.SelectMany(flowId =>
            Enumerable.Range(0, operationsPerFlow).Select(op => Task.Run(async () =>
            {
                switch (op % 4)
                {
                    case 0: // Get
                        await store.GetAsync<PerfTestState>(flowId);
                        Interlocked.Increment(ref getCount);
                        break;

                    case 1: // Update
                        var current = await store.GetAsync<PerfTestState>(flowId);
                        if (current != null)
                        {
                            current.State.Counter++;
                            await store.UpdateAsync(current);
                            Interlocked.Increment(ref updateCount);
                        }
                        break;

                    case 2: // Create new
                        var newId = $"{flowId}-new-{op}";
                        var snapshot = CreateSnapshot(newId);
                        await store.CreateAsync(snapshot);
                        Interlocked.Increment(ref createCount);
                        break;

                    case 3: // Delete
                        if (op == operationsPerFlow - 1) // Only on last operation
                        {
                            await store.DeleteAsync(flowId);
                            Interlocked.Increment(ref deleteCount);
                        }
                        break;
                }
            })));

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        var totalOperations = createCount + updateCount + deleteCount + getCount;

        _output.WriteLine($"Mixed Operations Performance:");
        _output.WriteLine($"  Total Operations: {totalOperations}");
        _output.WriteLine($"  Creates: {createCount}");
        _output.WriteLine($"  Updates: {updateCount}");
        _output.WriteLine($"  Deletes: {deleteCount}");
        _output.WriteLine($"  Gets: {getCount}");
        _output.WriteLine($"  Total Time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Operations/sec: {totalOperations * 1000.0 / stopwatch.ElapsedMilliseconds:F0}");
    }

    [Fact]
    public async Task Performance_MemoryUsage_LargeStateObjects()
    {
        var store = new InMemoryDslFlowStore();
        const int flowCount = 100;
        const int itemsPerFlow = 1000;

        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var baselineMemory = GC.GetTotalMemory(true);

        // Create flows with large state objects
        for (int i = 0; i < flowCount; i++)
        {
            var state = new PerfTestState
            {
                FlowId = $"memory-test-{i}",
                LargeData = Enumerable.Range(0, itemsPerFlow)
                    .Select(j => $"Item-{j}-{'x'.PadRight(100, 'x')}")
                    .ToList(),
                Dictionary = Enumerable.Range(0, itemsPerFlow)
                    .ToDictionary(j => $"key-{j}", j => (object)$"value-{j}")
            };

            var snapshot = CreateSnapshot($"memory-test-{i}", state);
            await store.CreateAsync(snapshot);
        }

        // Force GC to get accurate measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var usedMemory = GC.GetTotalMemory(true) - baselineMemory;
        var memoryPerFlow = usedMemory / flowCount;
        var memoryPerItem = usedMemory / (flowCount * itemsPerFlow);

        _output.WriteLine($"Memory Usage:");
        _output.WriteLine($"  Flow Count: {flowCount}");
        _output.WriteLine($"  Items per Flow: {itemsPerFlow}");
        _output.WriteLine($"  Total Memory: {usedMemory / (1024 * 1024):F2} MB");
        _output.WriteLine($"  Memory per Flow: {memoryPerFlow / 1024:F2} KB");
        _output.WriteLine($"  Memory per Item: {memoryPerItem} bytes");

        // Memory should be reasonable
        memoryPerItem.Should().BeLessThan(500, "each item should use less than 500 bytes");
    }

    // Helper methods
    private FlowSnapshot<PerfTestState> CreateSnapshot(string flowId, PerfTestState? state = null)
    {
        return new FlowSnapshot<PerfTestState>
        {
            FlowId = flowId,
            State = state ?? new PerfTestState { FlowId = flowId },
            Status = DslFlowStatus.Running,
            Position = new FlowPosition(new[] { 0 }),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
    }
}

// Performance test state
public class PerfTestState : IFlowState
{
    public string? FlowId { get; set; }
    public int Counter { get; set; }
    public List<string> LargeData { get; set; } = new();
    public Dictionary<string, object> Dictionary { get; set; } = new();

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
