using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Parallel processing scenario tests.
/// Tests concurrent execution, synchronization, and parallel workflow patterns.
/// </summary>
public class ParallelProcessingTests
{
    #region Test State

    public class ParallelState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public ConcurrentBag<string> ProcessedItems { get; set; } = new();
        public ConcurrentDictionary<string, DateTime> ProcessTimes { get; set; } = new();
        public int MaxConcurrency { get; set; }
        public int TotalProcessed => ProcessedItems.Count;
        public List<string> Errors { get; set; } = new();
    }

    #endregion

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ParallelForEach_ProcessesAllItems_Concurrently()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var currentConcurrent = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        var flow = FlowBuilder.Create<ParallelState>("parallel-foreach")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    lock (lockObj)
                    {
                        currentConcurrent++;
                        if (currentConcurrent > maxConcurrent)
                            maxConcurrent = currentConcurrent;
                    }

                    state.ProcessTimes[item] = DateTime.UtcNow;
                    await Task.Delay(50, ct); // Simulate work
                    state.ProcessedItems.Add(item);

                    lock (lockObj)
                    {
                        currentConcurrent--;
                    }

                    return true;
                }))
            .WithParallelism(5)
            .Step("complete", async (state, ct) =>
            {
                state.MaxConcurrency = maxConcurrent;
                return true;
            })
            .Build();

        var state = new ParallelState
        {
            FlowId = "parallel-test",
            Items = Enumerable.Range(1, 20).Select(i => $"item-{i}").ToList()
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(20);
        result.State.MaxConcurrency.Should().BeGreaterThan(1);
        result.State.MaxConcurrency.Should().BeLessOrEqualTo(5);
    }

    [Fact]
    public async Task ParallelForEach_WithSequentialSteps_MaintainsOrder()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var executionOrder = new ConcurrentBag<string>();

        var flow = FlowBuilder.Create<ParallelState>("parallel-with-sequential")
            .Step("before-parallel", async (state, ct) =>
            {
                executionOrder.Add("before");
                return true;
            })
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"parallel-{item}", async (state, ct) =>
                {
                    await Task.Delay(10, ct);
                    state.ProcessedItems.Add(item);
                    return true;
                }))
            .WithParallelism(3)
            .Step("after-parallel", async (state, ct) =>
            {
                executionOrder.Add("after");
                return true;
            })
            .Build();

        var state = new ParallelState
        {
            FlowId = "order-test",
            Items = new List<string> { "A", "B", "C", "D", "E" }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var orderList = executionOrder.ToList();
        orderList.First().Should().Be("before");
        orderList.Last().Should().Be("after");
    }

    [Fact]
    public async Task ParallelForEach_ErrorInOneItem_ContinuesOthers()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ParallelState>("parallel-error-handling")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    if (item == "item-3")
                    {
                        state.Errors.Add($"Error processing {item}");
                        return true; // Continue despite error
                    }

                    state.ProcessedItems.Add(item);
                    return true;
                }))
            .ContinueOnFailure()
            .Build();

        var state = new ParallelState
        {
            FlowId = "error-test",
            Items = new List<string> { "item-1", "item-2", "item-3", "item-4", "item-5" }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().HaveCount(4); // All except item-3
        result.State.Errors.Should().HaveCount(1);
    }

    [Fact]
    public async Task ParallelBranches_ExecutesConcurrently()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var executionTimes = new ConcurrentDictionary<string, DateTime>();

        var flow = FlowBuilder.Create<ParallelBranchState>("parallel-branches")
            .Step("start", async (state, ct) =>
            {
                executionTimes["start"] = DateTime.UtcNow;
                return true;
            })
            // Simulate parallel branches using ForEach
            .ForEach(
                s => s.Branches,
                (branch, f) => f.Step($"branch-{branch}", async (state, ct) =>
                {
                    executionTimes[branch] = DateTime.UtcNow;
                    await Task.Delay(100, ct);
                    state.CompletedBranches.Add(branch);
                    return true;
                }))
            .WithParallelism(3)
            .Step("join", async (state, ct) =>
            {
                executionTimes["join"] = DateTime.UtcNow;
                state.AllCompleted = state.CompletedBranches.Count == state.Branches.Count;
                return true;
            })
            .Build();

        var state = new ParallelBranchState
        {
            FlowId = "branch-test",
            Branches = new List<string> { "branch-A", "branch-B", "branch-C" }
        };

        // Act
        var startTime = DateTime.UtcNow;
        var result = await executor.ExecuteAsync(flow, state);
        var totalTime = DateTime.UtcNow - startTime;

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.AllCompleted.Should().BeTrue();
        result.State.CompletedBranches.Should().HaveCount(3);
        // If parallel, should complete faster than sequential (3 * 100ms = 300ms)
        totalTime.TotalMilliseconds.Should().BeLessThan(400); // Allow some overhead
    }

    [Fact]
    public async Task ParallelWithSharedResource_SynchronizesAccess()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<SharedResourceState>("shared-resource")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"update-{item}", async (state, ct) =>
                {
                    // Simulate atomic counter increment
                    Interlocked.Increment(ref state._counter);
                    state.ProcessedItems.Add(item);
                    return true;
                }))
            .WithParallelism(10)
            .Build();

        var state = new SharedResourceState
        {
            FlowId = "shared-test",
            Items = Enumerable.Range(1, 100).Select(i => $"item-{i}").ToList()
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Counter.Should().Be(100);
        result.State.ProcessedItems.Should().HaveCount(100);
    }

    [Fact]
    public async Task ParallelWithCancellation_StopsAllBranches()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var cts = new CancellationTokenSource();
        var startedItems = new ConcurrentBag<string>();

        var flow = FlowBuilder.Create<ParallelState>("cancel-parallel")
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"process-{item}", async (state, ct) =>
                {
                    startedItems.Add(item);

                    // Cancel after some items start
                    if (startedItems.Count >= 3)
                    {
                        cts.Cancel();
                    }

                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(100, ct);
                    state.ProcessedItems.Add(item);
                    return true;
                }))
            .WithParallelism(5)
            .Build();

        var state = new ParallelState
        {
            FlowId = "cancel-test",
            Items = Enumerable.Range(1, 10).Select(i => $"item-{i}").ToList()
        };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await executor.ExecuteAsync(flow, state, cts.Token);
        });

        // Some items should have started
        startedItems.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task ParallelWithDependentResults_AggregatesCorrectly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<AggregationState>("aggregate-parallel")
            .ForEach(
                s => s.Numbers,
                (num, f) => f.Step($"calculate-{num}", async (state, ct) =>
                {
                    var result = num * num; // Square
                    lock (state.Results)
                    {
                        state.Results.Add(result);
                    }
                    Interlocked.Add(ref state._sum, result);
                    return true;
                }))
            .WithParallelism(4)
            .Step("finalize", async (state, ct) =>
            {
                state.FinalSum = state.Sum;
                state.Average = (double)state.Sum / state.Numbers.Count;
                return true;
            })
            .Build();

        var state = new AggregationState
        {
            FlowId = "agg-test",
            Numbers = Enumerable.Range(1, 10).ToList() // 1-10
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // Sum of squares 1-10 = 385
        result.State.FinalSum.Should().Be(385);
        result.State.Results.Should().HaveCount(10);
    }

    public class ParallelBranchState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Branches { get; set; } = new();
        public ConcurrentBag<string> CompletedBranches { get; set; } = new();
        public bool AllCompleted { get; set; }
    }

    public class SharedResourceState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Items { get; set; } = new();
        public ConcurrentBag<string> ProcessedItems { get; set; } = new();
        internal int _counter;
        public int Counter => _counter;
    }

    public class AggregationState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<int> Numbers { get; set; } = new();
        public List<int> Results { get; set; } = new();
        internal int _sum;
        public int Sum => _sum;
        public int FinalSum { get; set; }
        public double Average { get; set; }
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
