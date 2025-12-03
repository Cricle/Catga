using Catga.Flow;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class FlowExecutorTests
{
    private readonly InMemoryFlowStore _store = new();
    private readonly FlowExecutor _executor;

    public FlowExecutorTests()
    {
        _executor = new FlowExecutor(_store, new FlowOptions
        {
            NodeId = "test-node",
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            ClaimTimeout = TimeSpan.FromSeconds(1)
        });
    }

    [Fact]
    public async Task ExecuteAsync_NewFlow_CreatesAndExecutes()
    {
        var executed = false;

        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                executed = true;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        result.FlowId.Should().Be("flow-1");
        executed.Should().BeTrue();

        var stored = await _store.GetAsync("flow-1");
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(FlowStatus.Done);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingDoneFlow_ReturnsImmediately()
    {
        // Pre-create a completed flow
        var state = new FlowState
        {
            Id = "flow-1",
            Type = "TestFlow",
            Status = FlowStatus.Done,
            Step = 3,
            Version = 0
        };
        await _store.CreateAsync(state);

        var executed = false;
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                executed = true;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeFalse(); // Should not execute again
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFailedFlow_ReturnsImmediately()
    {
        var state = new FlowState
        {
            Id = "flow-1",
            Type = "TestFlow",
            Status = FlowStatus.Failed,
            Step = 2,
            Error = "Previous error",
            Version = 0
        };
        await _store.CreateAsync(state);

        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) => new FlowResult(true, 1, TimeSpan.Zero));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Previous error");
    }

    [Fact]
    public async Task ExecuteAsync_FlowFails_UpdatesStatusToFailed()
    {
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(false, 1, TimeSpan.Zero, "Step failed"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Step failed");

        var stored = await _store.GetAsync("flow-1");
        stored!.Status.Should().Be(FlowStatus.Failed);
        stored.Error.Should().Be("Step failed");
    }

    [Fact]
    public async Task ExecuteAsync_Idempotent_SameFlowIdReturnsSameResult()
    {
        var executionCount = 0;

        // First execution
        var result1 = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                Interlocked.Increment(ref executionCount);
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        // Second execution with same ID
        var result2 = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                Interlocked.Increment(ref executionCount);
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        executionCount.Should().Be(1); // Only executed once
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_StopsExecution()
    {
        using var cts = new CancellationTokenSource();

        var task = _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                try
                {
                    await Task.Delay(5000, ct);
                    return new FlowResult(true, 1, TimeSpan.Zero);
                }
                catch (OperationCanceledException)
                {
                    return new FlowResult(false, 0, TimeSpan.Zero) { IsCancelled = true };
                }
            },
            cts.Token);

        cts.CancelAfter(50);

        var result = await task;
        // The executor should handle cancellation gracefully
        result.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_StoresData()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };

        await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            data,
            async (state, ct) =>
            {
                state.Data.Should().BeEquivalentTo(data);
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        var stored = await _store.GetAsync("flow-1");
        stored!.Data.Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesStepCount()
    {
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(true, 5, TimeSpan.FromMilliseconds(100)));

        var stored = await _store.GetAsync("flow-1");
        stored!.Step.Should().Be(5);
    }

    #region TDD: Additional Executor Tests

    [Fact]
    public async Task ExecuteAsync_ExceptionInExecutor_MarksFlowFailed()
    {
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            (state, ct) => throw new InvalidOperationException("Executor crashed"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Executor crashed");

        var stored = await _store.GetAsync("flow-1");
        stored!.Status.Should().Be(FlowStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleFlowTypes_Independent()
    {
        var typeAExecuted = false;
        var typeBExecuted = false;

        var resultA = await _executor.ExecuteAsync(
            "flow-a",
            "TypeA",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                typeAExecuted = true;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        var resultB = await _executor.ExecuteAsync(
            "flow-b",
            "TypeB",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                typeBExecuted = true;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        resultA.IsSuccess.Should().BeTrue();
        resultB.IsSuccess.Should().BeTrue();
        typeAExecuted.Should().BeTrue();
        typeBExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SetsOwner()
    {
        await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(true, 1, TimeSpan.Zero));

        var stored = await _store.GetAsync("flow-1");
        stored!.Owner.Should().Be("test-node");
    }

    [Fact]
    public async Task ExecuteAsync_PartialProgress_RecordsStep()
    {
        await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(false, 3, TimeSpan.Zero, "Failed at step 4"));

        var stored = await _store.GetAsync("flow-1");
        stored!.Step.Should().Be(3);
        stored.Status.Should().Be(FlowStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyData_HandledCorrectly()
    {
        byte[]? receivedData = null;

        await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                receivedData = state.Data;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        receivedData.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_LargeData_Preserved()
    {
        var largeData = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(largeData);
        byte[]? receivedData = null;

        await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            largeData,
            async (state, ct) =>
            {
                receivedData = state.Data;
                return new FlowResult(true, 1, TimeSpan.Zero);
            });

        receivedData.Should().BeEquivalentTo(largeData);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentSameFlowId_AllReturnSuccess()
    {
        var executionCount = 0;

        var tasks = Enumerable.Range(1, 5).Select(async _ =>
        {
            return await _executor.ExecuteAsync(
                "same-flow",
                "TestFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) =>
                {
                    Interlocked.Increment(ref executionCount);
                    await Task.Delay(50, ct);
                    return new FlowResult(true, 1, TimeSpan.Zero);
                });
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // All should return success (either executed or already done)
        results.All(r => r.IsSuccess).Should().BeTrue();
        // At least one execution happened
        executionCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_FlowResult_ContainsFlowId()
    {
        var result = await _executor.ExecuteAsync(
            "my-flow-id",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) => new FlowResult(true, 1, TimeSpan.Zero));

        result.FlowId.Should().Be("my-flow-id");
    }

    [Fact]
    public async Task ExecuteAsync_FlowResult_ContainsDuration()
    {
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (state, ct) =>
            {
                await Task.Delay(50, ct);
                return new FlowResult(true, 1, TimeSpan.FromMilliseconds(50));
            });

        result.Duration.TotalMilliseconds.Should().BeGreaterThan(40);
    }

    [Fact]
    public async Task ExecuteAsync_RunningFlow_OwnedBySameNode_Executes()
    {
        // Pre-create a running flow owned by same node
        var state = new FlowState
        {
            Id = "flow-1",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 1,
            Version = 0,
            Owner = "test-node",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await _store.CreateAsync(state);

        var executed = false;
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                executed = true;
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_RunningFlow_OwnedByDifferentNode_WaitsOrFails()
    {
        // Pre-create a running flow owned by different node with recent heartbeat
        var state = new FlowState
        {
            Id = "flow-1",
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 1,
            Version = 0,
            Owner = "other-node",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await _store.CreateAsync(state);

        var executed = false;
        var result = await _executor.ExecuteAsync(
            "flow-1",
            "TestFlow",
            ReadOnlyMemory<byte>.Empty,
            async (s, ct) =>
            {
                executed = true;
                return new FlowResult(true, 2, TimeSpan.Zero);
            });

        // Should not execute since another node owns it
        executed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_SequentialFlows_AllComplete()
    {
        for (int i = 1; i <= 10; i++)
        {
            var result = await _executor.ExecuteAsync(
                $"flow-{i}",
                "TestFlow",
                ReadOnlyMemory<byte>.Empty,
                async (state, ct) => new FlowResult(true, 1, TimeSpan.Zero));

            result.IsSuccess.Should().BeTrue();
        }

        // Verify all flows completed
        for (int i = 1; i <= 10; i++)
        {
            var stored = await _store.GetAsync($"flow-{i}");
            stored!.Status.Should().Be(FlowStatus.Done);
        }
    }

    #endregion
}
