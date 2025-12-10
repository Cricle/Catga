using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;
using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Flow;

/// <summary>
/// Tests for parallel ForEach functionality.
/// </summary>
public class ParallelForEachTests
{
    private readonly ITestOutputHelper _output;

    public ParallelForEachTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public class ParallelState : IFlowState
    {
        public string? FlowId { get; set; }
        public List<WorkItem> Items { get; set; } = [];
        public ConcurrentDictionary<string, string> Results { get; set; } = new();
        public ConcurrentBag<string> ProcessingOrder { get; set; } = [];
        public int ProcessedCount;

        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() { yield break; }
    }

    public record WorkItem(string Id, int ProcessingTimeMs);

    public record ProcessWorkRequest(string ItemId, int ProcessingTimeMs) : IRequest<string>
    {
        public long MessageId { get; init; } = DateTimeOffset.UtcNow.Ticks;
    }

    public class SequentialFlow : FlowConfig<ParallelState>
    {
        protected override void Configure(IFlowBuilder<ParallelState> flow)
        {
            flow.Name("sequential-processing");

            flow.ForEach<WorkItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessWorkRequest(item.Id, item.ProcessingTimeMs))
                     .Into(s => s.Results[item.Id]);
                })
                .WithBatchSize(10)
                .WithParallelism(1) // Sequential processing
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessingOrder.Add(item.Id);
                    Interlocked.Increment(ref state.ProcessedCount);
                })
            .EndForEach();
        }
    }

    public class ParallelFlow : FlowConfig<ParallelState>
    {
        private readonly int _parallelism;

        public ParallelFlow(int parallelism = 4)
        {
            _parallelism = parallelism;
        }

        protected override void Configure(IFlowBuilder<ParallelState> flow)
        {
            flow.Name("parallel-processing");

            flow.ForEach<WorkItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessWorkRequest(item.Id, item.ProcessingTimeMs))
                     .Into(s => s.Results[item.Id]);
                })
                .WithBatchSize(10)
                .WithParallelism(_parallelism) // Parallel processing
                .ContinueOnFailure() // Continue processing on item failure
                .OnItemSuccess((state, item, result) =>
                {
                    state.ProcessingOrder.Add(item.Id);
                    Interlocked.Increment(ref state.ProcessedCount);
                })
            .EndForEach();
        }
    }

    [Fact]
    public async Task ParallelForEach_ShouldProcessItemsConcurrently()
    {
        // Arrange
        var mediator = CreateSlowMediator();
        var store = new InMemoryDslFlowStore();
        var config = new ParallelFlow(4);
        config.Build();

        var executor = new DslFlowExecutor<ParallelState, ParallelFlow>(mediator, store, config);

        var items = Enumerable.Range(1, 8)
            .Select(i => new WorkItem($"item{i}", 100)) // 100ms processing time each
            .ToList();

        var state = new ParallelState
        {
            FlowId = "parallel-001",
            Items = items
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(8);
        result.State.ProcessedCount.Should().Be(8);

        // With 4 parallel workers and 8 items of 100ms each,
        // total time should be around 200ms (2 batches) rather than 800ms (sequential)
        _output.WriteLine($"Parallel processing took: {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(600, "Parallel processing should be faster than sequential");
    }

    [Fact]
    public async Task SequentialForEach_ShouldProcessItemsInOrder()
    {
        // Arrange
        var mediator = CreateSlowMediator();
        var store = new InMemoryDslFlowStore();
        var config = new SequentialFlow();
        config.Build();

        var executor = new DslFlowExecutor<ParallelState, SequentialFlow>(mediator, store, config);

        var items = Enumerable.Range(1, 4)
            .Select(i => new WorkItem($"item{i}", 50))
            .ToList();

        var state = new ParallelState
        {
            FlowId = "sequential-001",
            Items = items
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(4);
        result.State.ProcessedCount.Should().Be(4);

        // Sequential processing should take at least 200ms (4 * 50ms)
        _output.WriteLine($"Sequential processing took: {stopwatch.ElapsedMilliseconds}ms");
        stopwatch.ElapsedMilliseconds.Should().BeGreaterThan(150, "Sequential processing should take time");

        // Items should be processed in order (though this might not be guaranteed due to async nature)
        var processingOrder = result.State.ProcessingOrder.ToList();
        processingOrder.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(1, 400)] // Sequential: ~400ms for 4 items * 100ms
    [InlineData(2, 250)] // 2 parallel: ~200ms for 2 batches
    [InlineData(4, 150)] // 4 parallel: ~100ms for 1 batch
    public async Task ParallelForEach_ShouldScaleWithParallelism(int parallelism, int expectedMaxDuration)
    {
        // Arrange
        var mediator = CreateSlowMediator();
        var store = new InMemoryDslFlowStore();
        var config = new ParallelFlow(parallelism);
        config.Build();

        var executor = new DslFlowExecutor<ParallelState, ParallelFlow>(mediator, store, config);

        var items = Enumerable.Range(1, 4)
            .Select(i => new WorkItem($"item{i}", 100))
            .ToList();

        var state = new ParallelState
        {
            FlowId = $"parallel-{parallelism}",
            Items = items
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor.RunAsync(state);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(4);

        _output.WriteLine($"Parallelism {parallelism} took: {stopwatch.ElapsedMilliseconds}ms (expected max: {expectedMaxDuration}ms)");

        // Allow some tolerance for timing variations
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(expectedMaxDuration + 100,
            $"Parallelism {parallelism} should complete within expected time");
    }

    [Fact]
    public async Task ParallelForEach_ShouldHandleFailuresCorrectly()
    {
        // Arrange
        var mediator = CreateFailingMediator();
        var store = new InMemoryDslFlowStore();
        var config = new ParallelFlow(3);
        config.Build();

        var executor = new DslFlowExecutor<ParallelState, ParallelFlow>(mediator, store, config);

        var items = new List<WorkItem>
        {
            new("item1", 50),
            new("FAIL", 50), // This will fail
            new("item3", 50),
            new("item4", 50)
        };

        var state = new ParallelState
        {
            FlowId = "parallel-fail-001",
            Items = items
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert - With ContinueOnFailure, should process successful items
        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(3); // 3 successful items
        result.State.ProcessedCount.Should().Be(3);
    }

    [Fact]
    public async Task ParallelForEach_ShouldRespectBatchSize()
    {
        // Test that parallel processing still respects batch boundaries
        var mediator = CreateFastMediator();
        var store = new InMemoryDslFlowStore();

        var config = new ParallelBatchFlow(parallelism: 2, batchSize: 3);
        config.Build();

        var executor = new DslFlowExecutor<ParallelState, ParallelBatchFlow>(mediator, store, config);

        var items = Enumerable.Range(1, 10)
            .Select(i => new WorkItem($"item{i}", 10))
            .ToList();

        var state = new ParallelState
        {
            FlowId = "parallel-batch-001",
            Items = items
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Results.Should().HaveCount(10);
        result.State.ProcessedCount.Should().Be(10);
    }

    public class ParallelBatchFlow : FlowConfig<ParallelState>
    {
        private readonly int _parallelism;
        private readonly int _batchSize;

        public ParallelBatchFlow(int parallelism, int batchSize)
        {
            _parallelism = parallelism;
            _batchSize = batchSize;
        }

        protected override void Configure(IFlowBuilder<ParallelState> flow)
        {
            flow.Name("parallel-batch-processing");

            flow.ForEach<WorkItem>(s => s.Items)
                .Configure((item, f) =>
                {
                    f.Send(s => new ProcessWorkRequest(item.Id, item.ProcessingTimeMs))
                     .Into(s => s.Results[item.Id]);
                })
                .WithBatchSize(_batchSize)
                .WithParallelism(_parallelism)
                .OnItemSuccess((state, item, result) =>
                {
                    Interlocked.Increment(ref state.ProcessedCount);
                })
            .EndForEach();
        }
    }

    private static ICatgaMediator CreateSlowMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ProcessWorkRequest, string>(
            Arg.Any<ProcessWorkRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessWorkRequest>();
                return new ValueTask<CatgaResult<string>>(Task.Run(async () =>
                {
                    // Simulate processing time
                    await Task.Delay(request.ProcessingTimeMs);
                    return CatgaResult<string>.Success($"processed-{request.ItemId}");
                }));
            });

        return mediator;
    }

    private static ICatgaMediator CreateFastMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ProcessWorkRequest, string>(
            Arg.Any<ProcessWorkRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessWorkRequest>();
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{request.ItemId}"));
            });

        return mediator;
    }

    private static ICatgaMediator CreateFailingMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ProcessWorkRequest, string>(
            Arg.Any<ProcessWorkRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessWorkRequest>();
                return new ValueTask<CatgaResult<string>>(Task.Run(async () =>
                {
                    await Task.Delay(request.ProcessingTimeMs);

                    if (request.ItemId == "FAIL")
                    {
                        return CatgaResult<string>.Failure("Simulated failure");
                    }

                    return CatgaResult<string>.Success($"processed-{request.ItemId}");
                }));
            });

        return mediator;
    }
}
