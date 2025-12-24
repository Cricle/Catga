using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Flow;

/// <summary>
/// Performance and memory tests for ForEach functionality.
/// </summary>
public class ForEachPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public ForEachPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public class PerformanceState : IFlowState
    {
        public string? FlowId { get; set; }
        public List<TestItem> Items { get; set; } = [];
        public Dictionary<string, string> Results { get; set; } = [];
        public int ProcessedCount { get; set; }

        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    public record TestItem(string Id, string Data);

    public record ProcessTestItemRequest(string ItemId, string Data) : IRequest<string>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public class PerformanceTestFlow : FlowConfig<PerformanceState>
    {
        private readonly int _batchSize;

        public PerformanceTestFlow(int batchSize = 100)
        {
            _batchSize = batchSize;
        }

        protected override void Configure(IFlowBuilder<PerformanceState> flow)
        {
            flow.Name("performance-test-flow");

            flow.ForEach<TestItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessTestItemRequest(item.Id, item.Data));
                })
                .WithBatchSize(_batchSize)
                .OnItemSuccess((state, item, result) =>
                {
                    state.Results[item.Id] = result?.ToString() ?? "success";
                    state.ProcessedCount++;
                })
            .EndForEach();
        }
    }

    [Theory]
    [InlineData(100, 10)]      // Small collection, small batch
    [InlineData(1000, 50)]     // Medium collection, medium batch
    [InlineData(5000, 100)]    // Large collection, large batch
    [InlineData(10000, 200)]   // Very large collection, very large batch
    public async Task ForEach_PerformanceBenchmark(int itemCount, int batchSize)
    {
        // Arrange
        var mediator = CreateFastMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new PerformanceTestFlow(batchSize);
        config.Build();

        var executor = new DslFlowExecutor<PerformanceState, PerformanceTestFlow>(mediator, store, config);

        var items = GenerateTestItems(itemCount);
        var state = new PerformanceState
        {
            FlowId = $"perf-{itemCount}-{batchSize}",
            Items = items
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(true);

        var result = await executor.RunAsync(state);

        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = finalMemory - initialMemory;

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(itemCount);
        result.State.ProcessedCount.Should().Be(itemCount);

        // Performance metrics
        var itemsPerSecond = itemCount / stopwatch.Elapsed.TotalSeconds;
        var memoryPerItem = memoryUsed / (double)itemCount;

        _output.WriteLine($"Items: {itemCount}, Batch: {batchSize}");
        _output.WriteLine($"Duration: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Items/sec: {itemsPerSecond:F2}");
        _output.WriteLine($"Memory used: {memoryUsed / 1024.0:F2} KB");
        _output.WriteLine($"Memory/item: {memoryPerItem:F2} bytes");

        // Performance assertions
        itemsPerSecond.Should().BeGreaterThan(100, "Should process at least 100 items per second");
        memoryPerItem.Should().BeLessThan(30000, "Should use less than 30KB per item on average");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(1), "Should complete within 1 minute");
    }

    [Fact]
    public async Task ForEach_MemoryUsage_ShouldBeConstantWithBatching()
    {
        // This test verifies that memory usage doesn't grow linearly with collection size
        // when using appropriate batch sizes

        var mediator = CreateFastMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();

        var results = new List<(int ItemCount, long MemoryUsed, TimeSpan Duration)>();

        var itemCounts = new[] { 1000, 2000, 4000, 8000 };

        foreach (var itemCount in itemCounts)
        {
            var config = new PerformanceTestFlow(100); // Fixed batch size
            config.Build();

            var executor = new DslFlowExecutor<PerformanceState, PerformanceTestFlow>(mediator, store, config);

            var items = GenerateTestItems(itemCount);
            var state = new PerformanceState
            {
                FlowId = $"memory-{itemCount}",
                Items = items
            };

            // Force garbage collection before measurement
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var initialMemory = GC.GetTotalMemory(false);
            var stopwatch = Stopwatch.StartNew();

            var result = await executor.RunAsync(state);

            stopwatch.Stop();
            var finalMemory = GC.GetTotalMemory(false);
            var memoryUsed = finalMemory - initialMemory;

            results.Add((itemCount, memoryUsed, stopwatch.Elapsed));

            result.IsSuccess.Should().BeTrue();
            result.State.Results.Should().HaveCount(itemCount);

            _output.WriteLine($"Items: {itemCount}, Memory: {memoryUsed / 1024.0:F2} KB, Duration: {stopwatch.ElapsedMilliseconds}ms");
        }

        // Memory growth should be sub-linear (not proportional to item count)
        var memoryGrowthRatio = (double)results.Last().MemoryUsed / results.First().MemoryUsed;
        var itemGrowthRatio = (double)results.Last().ItemCount / results.First().ItemCount;

        _output.WriteLine($"Memory growth ratio: {memoryGrowthRatio:F2}");
        _output.WriteLine($"Item growth ratio: {itemGrowthRatio:F2}");

        // Memory should grow slower than item count (due to batching)
        memoryGrowthRatio.Should().BeLessThan(itemGrowthRatio * 0.85,
            "Memory usage should grow sub-linearly due to batching");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(500)]
    public async Task ForEach_BatchSize_ShouldAffectPerformance(int batchSize)
    {
        // Test how different batch sizes affect performance
        const int itemCount = 2000;

        var mediator = CreateFastMediator();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new PerformanceTestFlow(batchSize);
        config.Build();

        var executor = new DslFlowExecutor<PerformanceState, PerformanceTestFlow>(mediator, store, config);

        var items = GenerateTestItems(itemCount);
        var state = new PerformanceState
        {
            FlowId = $"batch-{batchSize}",
            Items = items
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(itemCount);

        var itemsPerSecond = itemCount / stopwatch.Elapsed.TotalSeconds;

        _output.WriteLine($"Batch size: {batchSize}, Items/sec: {itemsPerSecond:F2}, Duration: {stopwatch.ElapsedMilliseconds}ms");

        // All batch sizes should be reasonably performant
        itemsPerSecond.Should().BeGreaterThan(50, $"Batch size {batchSize} should process at least 50 items/sec");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), $"Batch size {batchSize} should complete within 30 seconds");
    }

    [Fact]
    public async Task ForEach_Recovery_ShouldNotSignificantlyImpactPerformance()
    {
        // Test that recovery mechanism doesn't significantly slow down processing
        const int itemCount = 1000;

        var store = TestStoreExtensions.CreateTestFlowStore();

        // Pre-populate some progress (simulate partial completion)
        var progress = new ForEachProgress
        {
            CurrentIndex = 500,
            TotalCount = itemCount,
            CompletedIndices = Enumerable.Range(0, 500).ToList(),
            FailedIndices = []
        };

        await store.SaveForEachProgressAsync("recovery-perf", 0, progress);

        var mediator = CreateFastMediator();
        var config = new PerformanceTestFlow(100);
        config.Build();

        var executor = new DslFlowExecutor<PerformanceState, PerformanceTestFlow>(mediator, store, config);

        var items = GenerateTestItems(itemCount);
        var state = new PerformanceState
        {
            FlowId = "recovery-perf",
            Items = items,
            // Pre-populate results for "completed" items
            Results = Enumerable.Range(0, 500)
                .ToDictionary(i => $"item-{i}", i => $"result-{i}"),
            ProcessedCount = 500
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(itemCount);

        // Should only process remaining 500 items
        var effectiveItemsPerSecond = 500 / stopwatch.Elapsed.TotalSeconds;

        _output.WriteLine($"Recovery performance: {effectiveItemsPerSecond:F2} items/sec for remaining 500 items");
        _output.WriteLine($"Duration: {stopwatch.ElapsedMilliseconds}ms");

        effectiveItemsPerSecond.Should().BeGreaterThan(100, "Recovery should not significantly impact performance");
    }

    private static List<TestItem> GenerateTestItems(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new TestItem($"item-{i}", $"data-{i}"))
            .ToList();
    }

    private static ICatgaMediator CreateFastMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ProcessTestItemRequest, string>(
            Arg.Any<ProcessTestItemRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessTestItemRequest>();
                // Simulate very fast processing
                return CatgaResult<string>.Success($"processed-{request.ItemId}");
            });

        return mediator;
    }
}
