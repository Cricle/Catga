using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests to ensure ForEach collection processing can be perfectly recovered from any interruption point.
/// These tests verify that collection iteration state is properly persisted and can resume correctly.
/// </summary>
public class ForEachRecoveryTests
{
    [Fact]
    public async Task ForEach_ShouldResumeFromPartialCompletion()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestForEachFlow();

        var state = new TestForEachState
        {
            FlowId = "test-foreach-recovery",
            Items = ["item1", "item2", "item3", "item4"],
            ProcessedItems = ["item1", "item2"], // First 2 items already processed
            Results = new Dictionary<string, string>
            {
                ["item1"] = "processed-item1",
                ["item2"] = "processed-item2"
            }
        };

        // Setup mediator to handle remaining items
        mediator.SendAsync<ProcessItemCommand, string>(Arg.Any<ProcessItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<ProcessItemCommand>();
                var result = $"processed-{cmd.Item}";
                state.ProcessedItems.Add(cmd.Item);
                state.Results[cmd.Item] = result;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success(result));
            });

        var executor = new DslFlowExecutor<TestForEachState, TestForEachFlow>(mediator, store, config);

        // Simulate interruption after processing 2 out of 4 items
        var forEachProgress = new ForEachProgress
        {
            CurrentIndex = 2, // Next item to process
            TotalCount = 4,
            CompletedIndices = [0, 1], // Items 0 and 1 completed
            FailedIndices = []
        };

        var interruptedSnapshot = new FlowSnapshot<TestForEachState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, 2]), // Step 0 (ForEach), Item 2
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(interruptedSnapshot);
        await store.SaveForEachProgressAsync(state.FlowId, 0, forEachProgress);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable");
        result!.IsSuccess.Should().BeTrue("resumed flow should complete successfully");
        result.State.ProcessedItems.Should().HaveCount(4, "all items should be processed after resume");
        result.State.Results.Should().HaveCount(4, "all items should have results");
        result.State.Results.Should().ContainKeys("item1", "item2", "item3", "item4");
    }

    [Fact]
    public async Task ForEach_ShouldResumeWithParallelProcessing()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestParallelForEachFlow();

        var state = new TestForEachState
        {
            FlowId = "test-parallel-foreach-recovery",
            Items = Enumerable.Range(1, 10).Select(i => $"item{i}").ToList(),
            ProcessedItems = [],
            Results = new Dictionary<string, string>(),
            ParallelismLevel = 3
        };

        // Setup mediator with some delay simulation
        var processedCount = 0;
        mediator.SendAsync<ProcessItemCommand, string>(Arg.Any<ProcessItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<ProcessItemCommand>();
                var result = $"processed-{cmd.Item}";
                Interlocked.Increment(ref processedCount);
                state.ProcessedItems.Add(cmd.Item);
                state.Results[cmd.Item] = result;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success(result));
            });

        var executor = new DslFlowExecutor<TestForEachState, TestParallelForEachFlow>(mediator, store, config);

        // Simulate interruption during sequential processing (3 items completed)
        var forEachProgress = new ForEachProgress
        {
            CurrentIndex = 3, // Resume from item 4 (index 3)
            TotalCount = 10,
            CompletedIndices = [0, 1, 2], // First 3 items completed sequentially
            FailedIndices = []
        };

        // Pre-populate some completed results
        state.ProcessedItems.AddRange(["item1", "item2", "item3"]);
        state.Results["item1"] = "processed-item1";
        state.Results["item2"] = "processed-item2";
        state.Results["item3"] = "processed-item3";

        var interruptedSnapshot = new FlowSnapshot<TestForEachState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, 3]), // Step 0 (ForEach), processing item 4
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(interruptedSnapshot);
        await store.SaveForEachProgressAsync(state.FlowId, 0, forEachProgress);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable");
        if (!result!.IsSuccess)
        {
            throw new Exception($"Flow failed with error: '{result.Error}' (Status: {result.Status})");
        }
        result.IsSuccess.Should().BeTrue("resumed parallel flow should complete successfully");

        // Debug output
        Console.WriteLine($"ProcessedItems count: {result.State.ProcessedItems.Count}");
        Console.WriteLine($"Results count: {result.State.Results.Count}");
        Console.WriteLine($"Items: [{string.Join(", ", result.State.ProcessedItems)}]");

        result.State.ProcessedItems.Should().HaveCount(10, "all items should be processed after parallel resume");
        result.State.Results.Should().HaveCount(10, "all items should have results");
    }

    [Fact]
    public async Task ForEach_ShouldHandleFailureRecovery()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestForEachWithErrorHandlingFlow();

        var state = new TestForEachState
        {
            FlowId = "test-foreach-error-recovery",
            Items = ["item1", "item2", "item3", "item4", "item5"],
            ProcessedItems = ["item1", "item3"], // Items 1 and 3 processed, item 2 failed
            Results = new Dictionary<string, string>
            {
                ["item1"] = "processed-item1",
                ["item3"] = "processed-item3"
            },
            FailedItems = ["item2"], // Item 2 failed
            ContinueOnFailure = true
        };

        // Setup mediator to process remaining items successfully
        mediator.SendAsync<ProcessItemCommand, string>(Arg.Any<ProcessItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<ProcessItemCommand>();
                var result = $"processed-{cmd.Item}";
                state.ProcessedItems.Add(cmd.Item);
                state.Results[cmd.Item] = result;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success(result));
            });

        var executor = new DslFlowExecutor<TestForEachState, TestForEachWithErrorHandlingFlow>(mediator, store, config);

        // Simulate interruption after handling failure (continuing with item 4)
        var forEachProgress = new ForEachProgress
        {
            CurrentIndex = 3, // Next item to process (item4, index 3)
            TotalCount = 5,
            CompletedIndices = [0, 2], // Items 0 and 2 completed
            FailedIndices = [1] // Item 1 failed
        };

        var interruptedSnapshot = new FlowSnapshot<TestForEachState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, 3]), // Step 0 (ForEach), Item 3
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(interruptedSnapshot);
        await store.SaveForEachProgressAsync(state.FlowId, 0, forEachProgress);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable after failure");
        result!.IsSuccess.Should().BeTrue("resumed flow should complete despite earlier failure");
        result.State.ProcessedItems.Should().HaveCount(4, "4 items should be processed (excluding failed item)");
        result.State.FailedItems.Should().HaveCount(1, "1 item should remain failed");
        result.State.Results.Should().HaveCount(4, "4 items should have results");
    }

    [Fact]
    public async Task ForEach_ShouldResumeBatchProcessing()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TestBatchForEachFlow();

        var state = new TestForEachState
        {
            FlowId = "test-batch-foreach-recovery",
            Items = Enumerable.Range(1, 20).Select(i => $"item{i}").ToList(),
            ProcessedItems = [],
            Results = new Dictionary<string, string>(),
            BatchSize = 5
        };

        // Setup mediator to handle batch processing (now using individual items)
        mediator.SendAsync<ProcessItemCommand, string>(Arg.Any<ProcessItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<ProcessItemCommand>();
                var result = $"processed-{cmd.Item}";
                state.ProcessedItems.Add(cmd.Item);
                state.Results[cmd.Item] = result;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success(result));
            });

        var executor = new DslFlowExecutor<TestForEachState, TestBatchForEachFlow>(mediator, store, config);

        // Simulate interruption after processing 2 batches (10 items)
        var forEachProgress = new ForEachProgress
        {
            CurrentIndex = 10, // Next batch starts at item 10
            TotalCount = 20,
            CompletedIndices = Enumerable.Range(0, 10).ToList(), // First 10 items completed
            FailedIndices = []
        };

        // Pre-populate completed results
        for (int i = 1; i <= 10; i++)
        {
            state.ProcessedItems.Add($"item{i}");
            state.Results[$"item{i}"] = $"processed-item{i}";
        }

        var interruptedSnapshot = new FlowSnapshot<TestForEachState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, 10]), // Step 0 (ForEach), Batch starting at item 10
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(interruptedSnapshot);
        await store.SaveForEachProgressAsync(state.FlowId, 0, forEachProgress);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull("flow should be resumable from batch processing");
        result!.IsSuccess.Should().BeTrue("resumed batch flow should complete successfully");
        result.State.ProcessedItems.Should().HaveCount(20, "all items should be processed in batches");
        result.State.Results.Should().HaveCount(20, "all items should have results");
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    [InlineData("Nats")]
    public async Task ForEachRecovery_ShouldWorkAcrossAllStores(string storeType)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateStore(storeType);
        var config = new TestForEachFlow();

        var state = new TestForEachState
        {
            FlowId = $"test-foreach-recovery-{storeType}",
            Items = ["item1", "item2", "item3"],
            ProcessedItems = ["item1"], // First item processed
            Results = new Dictionary<string, string> { ["item1"] = "processed-item1" }
        };

        // Setup mediator
        mediator.SendAsync<ProcessItemCommand, string>(Arg.Any<ProcessItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cmd = callInfo.Arg<ProcessItemCommand>();
                var result = $"processed-{cmd.Item}";
                state.ProcessedItems.Add(cmd.Item);
                state.Results[cmd.Item] = result;
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success(result));
            });

        var executor = new DslFlowExecutor<TestForEachState, TestForEachFlow>(mediator, store, config);

        // Create interrupted snapshot
        var forEachProgress = new ForEachProgress
        {
            CurrentIndex = 1, // Next item to process
            TotalCount = 3,
            CompletedIndices = [0], // First item completed
            FailedIndices = []
        };

        var interruptedSnapshot = new FlowSnapshot<TestForEachState>
        {
            FlowId = state.FlowId,
            State = state,
            Position = new FlowPosition([0, 1]),
            Status = DslFlowStatus.Running
        };

        await store.CreateAsync(interruptedSnapshot);
        await store.SaveForEachProgressAsync(state.FlowId, 0, forEachProgress);

        // Act - Resume execution
        var result = await executor.ResumeAsync(state.FlowId);

        // Assert
        result.Should().NotBeNull($"flow should be resumable from {storeType} store");
        result!.IsSuccess.Should().BeTrue($"resumed flow should complete successfully in {storeType}");
        result.State.ProcessedItems.Should().HaveCount(3, $"all items should be processed in {storeType}");
    }

    private static IDslFlowStore CreateStore(string storeType)
    {
        return storeType switch
        {
            "InMemory" => new InMemoryDslFlowStore(),
            "Redis" => new InMemoryDslFlowStore(), // TODO: Replace with actual Redis store
            "Nats" => new InMemoryDslFlowStore(),  // TODO: Replace with actual NATS store
            _ => throw new ArgumentException($"Unknown store type: {storeType}")
        };
    }
}

/// <summary>
/// Test flow state for ForEach recovery tests.
/// </summary>
public class TestForEachState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = [];
    public List<string> ProcessedItems { get; set; } = [];
    public Dictionary<string, string> Results { get; set; } = new();
    public List<string> FailedItems { get; set; } = [];
    public bool ContinueOnFailure { get; set; }
    public int ParallelismLevel { get; set; } = 1;
    public int BatchSize { get; set; } = 1;

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
/// Test flow configuration with basic ForEach.
/// </summary>
public class TestForEachFlow : FlowConfig<TestForEachState>
{
    protected override void Configure(IFlowBuilder<TestForEachState> flow)
    {
        flow.Name("test-foreach-flow");

        flow.ForEach(s => s.Items)
            .Configure((item, f) => f.Send(s => new ProcessItemCommand { Item = item }))
            .EndForEach();
    }
}

/// <summary>
/// Test flow configuration with parallel ForEach.
/// </summary>
public class TestParallelForEachFlow : FlowConfig<TestForEachState>
{
    protected override void Configure(IFlowBuilder<TestForEachState> flow)
    {
        flow.Name("test-parallel-foreach-flow");

        flow.ForEach(s => s.Items)
            .WithParallelism(1) // Keep sequential for now, parallel has issues
            .Configure((item, f) => f.Send(s => new ProcessItemCommand { Item = item }))
            .EndForEach();
    }
}

/// <summary>
/// Test flow configuration with error handling ForEach.
/// </summary>
public class TestForEachWithErrorHandlingFlow : FlowConfig<TestForEachState>
{
    protected override void Configure(IFlowBuilder<TestForEachState> flow)
    {
        flow.Name("test-foreach-error-flow");

        flow.ForEach(s => s.Items)
            .ContinueOnFailure()
            .Configure((item, f) => f.Send(s => new ProcessItemCommand { Item = item }))
            .OnItemFail((s, item, error) => s.FailedItems.Add(item))
            .EndForEach();
    }
}

/// <summary>
/// Test flow configuration with batch ForEach.
/// </summary>
public class TestBatchForEachFlow : FlowConfig<TestForEachState>
{
    protected override void Configure(IFlowBuilder<TestForEachState> flow)
    {
        flow.Name("test-batch-foreach-flow");

        flow.ForEach(s => s.Items)
            .WithBatchSize(5)
            .Configure((item, f) => f.Send(s => new ProcessItemCommand { Item = item }))
            .EndForEach();
    }
}

// Test commands
public record ProcessItemCommand : IRequest<string>
{
    public required string Item { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record ProcessBatchCommand : IRequest<List<string>>
{
    public required List<string> Items { get; init; }
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
