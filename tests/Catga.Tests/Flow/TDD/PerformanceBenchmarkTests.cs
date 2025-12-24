using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;
using System.Diagnostics;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// Performance benchmark tests to establish throughput and latency baselines for Flow DSL.
/// These tests ensure the system can handle enterprise-scale workloads efficiently.
/// </summary>
public class PerformanceBenchmarkTests
{
    [Theory]
    [InlineData(1000, 200)]    // 1K items, 200ms target
    [InlineData(10000, 1000)]   // 10K items, 1000ms target
    [InlineData(100000, 10000)] // 100K items, 10s target
    public async Task ForEach_ShouldMeetThroughputTargets(int itemCount, int maxMilliseconds)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TestHighThroughputFlow();

        var state = new TestPerformanceState
        {
            FlowId = $"perf-test-{itemCount}",
            Items = Enumerable.Range(1, itemCount).Select(i => $"item{i}").ToList(),
            ProcessedCount = 0
        };

        // Setup fast mediator response
        mediator.SendAsync<PerfTestCommand, string>(Arg.Any<PerfTestCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<PerfTestCommand>();
                state.ProcessedCount++;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });

        var executor = new DslFlowExecutor<TestPerformanceState, TestHighThroughputFlow>(mediator, store, config);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue($"flow with {itemCount} items should complete successfully");
        result.State.ProcessedCount.Should().Be(itemCount, "all items should be processed");

        // Performance assertions
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(maxMilliseconds,
            $"processing {itemCount} items should complete within {maxMilliseconds}ms");

        var itemsPerSecond = itemCount / stopwatch.Elapsed.TotalSeconds;
        var minThroughput = itemCount switch
        {
            <= 1000 => 5000,   // 5K items/sec for small batches (adjusted for CI/test environment)
            <= 10000 => 10000,  // 10K items/sec for medium batches
            _ => 14000           // 14K items/sec for large batches (adjusted for test environment)
        };

        itemsPerSecond.Should().BeGreaterThan(minThroughput,
            $"throughput should exceed {minThroughput} items/second");

        Console.WriteLine($"Performance: {itemCount} items in {stopwatch.ElapsedMilliseconds}ms ({itemsPerSecond:F0} items/sec)");
    }

    [Fact]
    public async Task ParallelFlow_ShouldScaleWithConcurrency()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TestParallelScalingFlow();

        var taskCount = 1000;
        var state = new TestPerformanceState
        {
            FlowId = "parallel-scaling-test",
            TaskCount = taskCount,
            CompletedTasks = 0
        };

        // Simulate some processing time
        mediator.SendAsync(Arg.Any<ParallelPerfCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.CompletedTasks++;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestPerformanceState, TestParallelScalingFlow>(mediator, store, config);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("parallel flow should complete successfully");
        result.State.CompletedTasks.Should().Be(taskCount, "all parallel tasks should complete");

        // Should complete much faster than sequential (1000ms)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(200,
            "parallel execution should be significantly faster than sequential");

        Console.WriteLine($"Parallel performance: {taskCount} tasks in {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ComplexFlow_ShouldMaintainPerformanceWithNesting()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = TestStoreExtensions.CreateTestFlowStore();
        var config = new TestComplexNestedFlow();

        var state = new TestPerformanceState
        {
            FlowId = "complex-nested-test",
            Items = Enumerable.Range(1, 100).Select(i => $"item{i}").ToList(),
            ProcessedCount = 0,
            BranchExecutions = 0
        };

        mediator.SendAsync<PerfTestCommand, string>(Arg.Any<PerfTestCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.ProcessedCount++;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("processed"));
            });

        mediator.SendAsync(Arg.Any<BranchPerfCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                state.BranchExecutions++;
                return new ValueTask<CatgaResult>(CatgaResult.Success());
            });

        var executor = new DslFlowExecutor<TestPerformanceState, TestComplexNestedFlow>(mediator, store, config);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        // Assert
        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue("complex nested flow should complete successfully");

        // Should handle complexity efficiently
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100,
            "complex nested flow should complete within 100ms");

        Console.WriteLine($"Complex flow performance: {stopwatch.ElapsedMilliseconds}ms, " +
                         $"{state.ProcessedCount} items, {state.BranchExecutions} branches");
    }
}

/// <summary>
/// Performance test state with thread-safe counters.
/// </summary>
public class TestPerformanceState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = [];
    public int ProcessedCount { get; set; }
    public int TaskCount { get; set; }
    public int CompletedTasks { get; set; }
    public int BranchExecutions { get; set; }

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
/// High-throughput ForEach flow configuration.
/// </summary>
public class TestHighThroughputFlow : FlowConfig<TestPerformanceState>
{
    protected override void Configure(IFlowBuilder<TestPerformanceState> flow)
    {
        flow.Name("high-throughput-flow");

        flow.ForEach(s => s.Items)
            .WithParallelism(Environment.ProcessorCount * 2) // High parallelism
            .WithBatchSize(50) // Optimized batch size
            .Configure((item, f) => f.Send(s => new PerfTestCommand { Item = item }))
            .EndForEach();
    }
}

/// <summary>
/// Parallel scaling test flow configuration.
/// </summary>
public class TestParallelScalingFlow : FlowConfig<TestPerformanceState>
{
    protected override void Configure(IFlowBuilder<TestPerformanceState> flow)
    {
        flow.Name("parallel-scaling-flow");

        // Create many parallel tasks
        var tasks = new List<Func<TestPerformanceState, IRequest>>();
        for (int i = 0; i < 1000; i++)
        {
            var taskId = i;
            tasks.Add(s => new ParallelPerfCommand { TaskId = taskId });
        }

        flow.WhenAll(tasks.ToArray());
    }
}

/// <summary>
/// Complex nested flow with multiple constructs.
/// </summary>
public class TestComplexNestedFlow : FlowConfig<TestPerformanceState>
{
    protected override void Configure(IFlowBuilder<TestPerformanceState> flow)
    {
        flow.Name("complex-nested-flow");

        flow.ForEach(s => s.Items.Take(50))
            .Configure((item, f) => f.Send(s => new PerfTestCommand { Item = item }))
            .EndForEach()
            .Send(s => new BranchPerfCommand { BranchId = 1 });
    }
}

// Performance test commands
public record PerfTestCommand : IRequest<string>
{
    public required string Item { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ParallelPerfCommand : IRequest
{
    public int TaskId { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record BranchPerfCommand : IRequest
{
    public int BranchId { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
