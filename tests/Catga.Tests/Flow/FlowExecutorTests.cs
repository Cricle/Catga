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
}
