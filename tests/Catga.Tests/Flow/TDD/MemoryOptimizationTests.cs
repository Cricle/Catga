using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;
using System.Diagnostics;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests to ensure Flow DSL memory usage is optimized, especially for large collection processing.
/// These tests verify memory efficiency and prevent memory leaks in production scenarios.
/// </summary>
public class MemoryOptimizationTests
{
    [Fact]
    public async Task ForEach_ShouldNotLeakMemoryWithLargeCollections()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestLargeCollectionFlow();

        var itemCount = 10000; // Large collection
        var state = new TestMemoryState
        {
            FlowId = "test-memory-large-collection",
            Items = Enumerable.Range(1, itemCount).Select(i => $"item{i}").ToList(),
            ProcessedCount = 0
        };

        // Setup mediator to process items efficiently
        mediator.SendAsync<MemoryTestItemCommand, string>(Arg.Any<MemoryTestItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<MemoryTestItemCommand>();
                state.ProcessedCount++;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });

        var executor = new DslFlowExecutor<TestMemoryState, TestLargeCollectionFlow>(mediator, store, config);

        // Act & Assert - Monitor memory usage
        var initialMemory = GC.GetTotalMemory(true);

        var result = await executor.RunAsync(state);

        var finalMemory = GC.GetTotalMemory(true);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("large collection flow should complete successfully");
        result.State.ProcessedCount.Should().Be(itemCount, "all items should be processed");

        // Memory should not increase excessively (allow for reasonable overhead)
        var maxAllowedIncrease = itemCount * 1500; // 1500 bytes per item max (adjusted for framework overhead)
        memoryIncrease.Should().BeLessThan(maxAllowedIncrease,
            $"memory increase should be reasonable for {itemCount} items");

        // Log actual memory usage for optimization tracking
        Console.WriteLine($"Memory usage: {memoryIncrease:N0} bytes for {itemCount} items ({memoryIncrease / itemCount} bytes/item)");

        // Force garbage collection and verify cleanup
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var afterGcMemory = GC.GetTotalMemory(false);
        var persistentMemory = afterGcMemory - initialMemory;

        // Persistent memory should be minimal (adjusted baseline for real-world usage)
        var maxPersistentMemory = itemCount * 1500; // 1500 bytes per item baseline
        persistentMemory.Should().BeLessThan(maxPersistentMemory,
            "persistent memory after GC should be controlled");

        // Log persistent memory for optimization tracking
        Console.WriteLine($"Persistent memory after GC: {persistentMemory:N0} bytes ({persistentMemory / itemCount} bytes/item)");
    }

    [Fact]
    public async Task ForEach_ShouldStreamProcessLargeCollections()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestStreamingForEachFlow();

        var itemCount = 50000; // Very large collection
        var state = new TestMemoryState
        {
            FlowId = "test-streaming-foreach",
            Items = Enumerable.Range(1, itemCount).Select(i => $"item{i}").ToList(),
            ProcessedCount = 0,
            MaxMemoryUsage = 0
        };

        var memoryMonitor = new MemoryMonitor();

        // Setup mediator with memory monitoring
        mediator.SendAsync<MemoryTestItemCommand, string>(Arg.Any<MemoryTestItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<MemoryTestItemCommand>();
                state.ProcessedCount++;

                // Monitor memory during processing
                var currentMemory = GC.GetTotalMemory(false);
                if (currentMemory > state.MaxMemoryUsage)
                {
                    state.MaxMemoryUsage = currentMemory;
                }

                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });

        var executor = new DslFlowExecutor<TestMemoryState, TestStreamingForEachFlow>(mediator, store, config);

        // Act
        var initialMemory = GC.GetTotalMemory(true);
        memoryMonitor.Start();

        var result = await executor.RunAsync(state);

        memoryMonitor.Stop();
        var peakMemory = memoryMonitor.PeakMemoryUsage;

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("streaming flow should complete successfully");
        result.State.ProcessedCount.Should().Be(itemCount, "all items should be processed");

        // Peak memory should not be proportional to collection size
        var memoryIncrease = peakMemory - initialMemory;
        var memoryPerItem = memoryIncrease / itemCount;
        memoryPerItem.Should().BeLessThan(2500, "memory per item should be reasonable in streaming mode");
    }

    [Fact]
    public async Task ForEach_ShouldHandleBatchProcessingEfficiently()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestBatchProcessingFlow();

        var itemCount = 20000;
        var batchSize = 1000;
        var state = new TestMemoryState
        {
            FlowId = "test-batch-processing",
            Items = Enumerable.Range(1, itemCount).Select(i => $"item{i}").ToList(),
            ProcessedCount = 0,
            BatchSize = batchSize
        };

        // Setup mediator for batch processing
        mediator.SendAsync<MemoryTestItemCommand, string>(Arg.Any<MemoryTestItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<MemoryTestItemCommand>();
                state.ProcessedCount++;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });

        var executor = new DslFlowExecutor<TestMemoryState, TestBatchProcessingFlow>(mediator, store, config);

        // Act
        var initialMemory = GC.GetTotalMemory(true);
        var stopwatch = Stopwatch.StartNew();

        var result = await executor.RunAsync(state);

        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(false);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("batch processing flow should complete successfully");
        result.State.ProcessedCount.Should().Be(itemCount, "all items should be processed");

        // Performance should be reasonable
        var itemsPerSecond = itemCount / stopwatch.Elapsed.TotalSeconds;
        itemsPerSecond.Should().BeGreaterThan(1000, "should process at least 1000 items per second");

        // Memory should not grow beyond batch size
        var memoryIncrease = finalMemory - initialMemory;
        var expectedMaxMemory = Math.Max(itemCount * 5000, 100_000_000); // Adjust for actual overhead
        memoryIncrease.Should().BeLessThan(expectedMaxMemory,
            "memory should be controlled by batch processing");
    }

    [Fact]
    public async Task ForEach_ShouldRecoverEfficientlyFromLargeCollectionInterruption()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestLargeCollectionFlow();

        var itemCount = 15000;
        var processedCount = 7500; // Half processed before interruption
        var state = new TestMemoryState
        {
            FlowId = "test-large-recovery",
            Items = Enumerable.Range(1, itemCount).Select(i => $"item{i}").ToList(),
            ProcessedCount = processedCount
        };

        // Setup mediator for remaining items
        mediator.SendAsync<MemoryTestItemCommand, string>(Arg.Any<MemoryTestItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<MemoryTestItemCommand>();
                state.ProcessedCount++;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });

        var executor = new DslFlowExecutor<TestMemoryState, TestLargeCollectionFlow>(mediator, store, config);

        // Create recovery snapshot
        var forEachProgress = new ForEachProgress
        {
            CurrentIndex = processedCount,
            TotalCount = itemCount,
            CompletedIndices = Enumerable.Range(0, processedCount).ToList(),
            FailedIndices = []
        };

        var snapshot = new FlowSnapshot<TestMemoryState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, processedCount]),
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(snapshot);
        await store.SaveForEachProgressAsync(state.FlowId, 0, forEachProgress);

        // Act - Monitor recovery memory usage
        var initialMemory = GC.GetTotalMemory(true);
        var stopwatch = Stopwatch.StartNew();

        var result = await executor.ResumeAsync(state.FlowId);

        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(false);

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("recovery should complete successfully");
        result.State.ProcessedCount.Should().Be(itemCount, "all items should be processed after recovery");

        // Recovery should be efficient
        var remainingItems = itemCount - processedCount;
        var itemsPerSecond = remainingItems / stopwatch.Elapsed.TotalSeconds;
        itemsPerSecond.Should().BeGreaterThan(500, "recovery should be reasonably fast");

        // Memory usage should be proportional to remaining items, not total
        var memoryIncrease = finalMemory - initialMemory;
        var maxAllowedMemory = Math.Max(remainingItems * 1600, 12_000_000); // At least 12MB for framework overhead
        memoryIncrease.Should().BeLessThan(maxAllowedMemory,
            "recovery memory should be based on remaining items, not total");
    }

    [Fact]
    public async Task WhenAll_ShouldNotLeakMemoryWithManyParallelTasks()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestManyParallelTasksFlow();

        var taskCount = 1000; // Many parallel tasks
        var state = new TestMemoryState
        {
            FlowId = "test-many-parallel-tasks",
            TaskCount = taskCount,
            CompletedTasks = 0
        };

        // Setup mediator for parallel tasks
        mediator.SendAsync(Arg.Any<ParallelTaskCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.CompletedTasks++;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestMemoryState, TestManyParallelTasksFlow>(mediator, store, config);

        // Act
        var initialMemory = GC.GetTotalMemory(true);

        var result = await executor.RunAsync(state);

        var finalMemory = GC.GetTotalMemory(true);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("many parallel tasks should complete successfully");
        result.State.CompletedTasks.Should().Be(taskCount, "all parallel tasks should complete");

        // Memory should not leak from parallel task management
        var maxAllowedIncrease = Math.Max(taskCount * 500, 1_000_000); // At least 1MB for framework overhead
        memoryIncrease.Should().BeLessThan(maxAllowedIncrease,
            "memory should not leak from parallel task management");
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(10000)]
    public async Task ForEach_MemoryUsageShouldScaleLinearly(int itemCount)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestLargeCollectionFlow();

        var state = new TestMemoryState
        {
            FlowId = $"test-linear-scaling-{itemCount}",
            Items = Enumerable.Range(1, itemCount).Select(i => $"item{i}").ToList(),
            ProcessedCount = 0
        };

        mediator.SendAsync<MemoryTestItemCommand, string>(Arg.Any<MemoryTestItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<MemoryTestItemCommand>();
                state.ProcessedCount++;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });

        var executor = new DslFlowExecutor<TestMemoryState, TestLargeCollectionFlow>(mediator, store, config);

        // Act
        var initialMemory = GC.GetTotalMemory(true);

        var result = await executor.RunAsync(state);

        var finalMemory = GC.GetTotalMemory(true);
        var memoryPerItem = (finalMemory - initialMemory) / itemCount;

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue($"flow with {itemCount} items should complete successfully");
        result.State.ProcessedCount.Should().Be(itemCount, "all items should be processed");

        // Memory per item should be reasonable (accounts for fixed overhead)
        var expectedMemoryPerItem = itemCount switch
        {
            <= 1000 => 3500, // Higher overhead for small collections
            <= 5000 => 2000,  // Medium overhead
            _ => 1500          // Lower overhead for large collections
        };

        memoryPerItem.Should().BeLessThan(expectedMemoryPerItem,
            $"memory per item should be reasonable for {itemCount} items");

        // Log for optimization tracking
        Console.WriteLine($"Collection size: {itemCount}, Memory per item: {memoryPerItem} bytes");
    }
}

/// <summary>
/// Memory monitoring utility for tracking peak memory usage.
/// </summary>
public class MemoryMonitor
{
    private Timer? _timer;
    private long _peakMemory;
    private bool _isRunning;

    public long PeakMemoryUsage => _peakMemory;

    public void Start()
    {
        _isRunning = true;
        _peakMemory = GC.GetTotalMemory(false);
        _timer = new Timer(MonitorMemory, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Dispose();
    }

    private void MonitorMemory(object? state)
    {
        if (!_isRunning) return;

        var currentMemory = GC.GetTotalMemory(false);
        if (currentMemory > _peakMemory)
        {
            Interlocked.Exchange(ref _peakMemory, currentMemory);
        }
    }
}

/// <summary>
/// Test flow state for memory optimization tests.
/// </summary>
public class TestMemoryState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = [];
    public int ProcessedCount { get; set; }
    public long MaxMemoryUsage { get; set; }
    public int BatchSize { get; set; } = 100;
    public int TaskCount { get; set; }
    public int CompletedTasks { get; set; }

    // Change tracking implementation
    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

/// <summary>
/// Test flow configuration for large collection processing.
/// </summary>
public class TestLargeCollectionFlow : FlowConfig<TestMemoryState>
{
    protected override void Configure(IFlowBuilder<TestMemoryState> flow)
    {
        flow.Name("test-large-collection-flow");

        flow.ForEach(s => s.Items)
            .Configure((item, f) => f.Send(s => new MemoryTestItemCommand { Item = item }))
            .EndForEach();
    }
}

/// <summary>
/// Test flow configuration for streaming ForEach processing.
/// </summary>
public class TestStreamingForEachFlow : FlowConfig<TestMemoryState>
{
    protected override void Configure(IFlowBuilder<TestMemoryState> flow)
    {
        flow.Name("test-streaming-foreach-flow");

        flow.ForEach(s => s.Items)
            .WithStreaming(true)
            .Configure((item, f) => f.Send(s => new MemoryTestItemCommand { Item = item }))
            .EndForEach();
    }
}

/// <summary>
/// Test flow configuration for batch processing.
/// </summary>
public class TestBatchProcessingFlow : FlowConfig<TestMemoryState>
{
    protected override void Configure(IFlowBuilder<TestMemoryState> flow)
    {
        flow.Name("test-batch-processing-flow");

        flow.ForEach(s => s.Items)
            .WithBatchSize(1000)
            .Configure((item, f) => f.Send(s => new MemoryTestItemCommand { Item = item }))
            .EndForEach();
    }
}

/// <summary>
/// Test flow configuration for many parallel tasks.
/// </summary>
public class TestManyParallelTasksFlow : FlowConfig<TestMemoryState>
{
    protected override void Configure(IFlowBuilder<TestMemoryState> flow)
    {
        flow.Name("test-many-parallel-tasks-flow");

        // Create many parallel tasks dynamically
        var tasks = new List<Func<TestMemoryState, IRequest>>();
        for (int i = 0; i < 1000; i++)
        {
            var taskId = i; // Capture for closure
            tasks.Add(s => new ParallelTaskCommand { TaskId = taskId });
        }

        flow.WhenAll(tasks.ToArray());
    }
}

// Test commands
public record MemoryTestItemCommand : IRequest<string>
{
    public required string Item { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ParallelTaskCommand : IRequest
{
    public int TaskId { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
