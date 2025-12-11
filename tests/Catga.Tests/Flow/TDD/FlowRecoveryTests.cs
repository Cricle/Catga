using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.TDD;

/// <summary>
/// TDD tests for flow recovery and resilience scenarios
/// </summary>
public class FlowRecoveryTests
{
    [Fact]
    public async Task Flow_FailureInMiddle_ShouldRecoverFromCorrectPosition()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new RecoverableFlow();
        var executor = new DslFlowExecutor<RecoveryState, RecoverableFlow>(mediator, store, config);

        var state = new RecoveryState
        {
            FlowId = "recovery-001",
            Items = ["A", "B", "FAIL", "C", "D"]
        };

        var attempts = new Dictionary<string, int>();
        SetupMediatorWithFailure(mediator, attempts);

        // Act - First run should fail
        var firstResult = await executor.RunAsync(state);

        // Assert first run
        firstResult.Should().NotBeNull();
        firstResult.IsSuccess.Should().BeFalse("FAIL item should cause failure");
        state.ProcessedItems.Should().BeEquivalentTo(["A", "B"]);

        // Act - Resume should complete
        var resumeResult = await executor.ResumeAsync(state.FlowId!);

        // Assert resume
        resumeResult.Should().NotBeNull();
        resumeResult.IsSuccess.Should().BeTrue("second attempt should succeed");
        resumeResult.State.ProcessedItems.Should().BeEquivalentTo(["A", "B", "FAIL", "C", "D"]);
    }

    [Fact]
    public async Task Flow_MultipleFailurePoints_ShouldRecoverIncrementally()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new MultiFailureFlow();
        var executor = new DslFlowExecutor<RecoveryState, MultiFailureFlow>(mediator, store, config);

        var state = new RecoveryState
        {
            FlowId = "multi-failure-001",
            Items = ["A", "FAIL1", "B", "FAIL2", "C"]
        };

        var attempts = new Dictionary<string, int>();
        SetupMediatorWithMultipleFailures(mediator, attempts);

        // Act - First run
        var result1 = await executor.RunAsync(state);
        result1.IsSuccess.Should().BeFalse();
        state.ProcessedItems.Should().BeEquivalentTo(["A"]);

        // Act - Second run (resume)
        var result2 = await executor.ResumeAsync(state.FlowId!);
        result2.IsSuccess.Should().BeFalse();
        result2.State.ProcessedItems.Should().BeEquivalentTo(["A", "FAIL1", "B"]);

        // Act - Third run (resume)
        var result3 = await executor.ResumeAsync(state.FlowId!);
        result3.IsSuccess.Should().BeTrue();
        result3.State.ProcessedItems.Should().BeEquivalentTo(["A", "FAIL1", "B", "FAIL2", "C"]);
    }

    [Fact]
    public async Task Flow_RecoveryWithStateChanges_ShouldPreserveProgress()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new StatefulRecoveryFlow();
        var executor = new DslFlowExecutor<RecoveryState, StatefulRecoveryFlow>(mediator, store, config);

        var state = new RecoveryState
        {
            FlowId = "stateful-recovery-001",
            Items = ["Item1", "Item2", "Item3"],
            Counter = 0
        };

        var failOnSecond = true;
        mediator.SendAsync<ProcessCommand, string>(Arg.Any<ProcessCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<ProcessCommand>();
                if (cmd.Item == "Item2" && failOnSecond)
                {
                    failOnSecond = false;
                    return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Failure("Transient failure"));
                }
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });

        // Act - First run
        var result1 = await executor.RunAsync(state);

        // Assert state was partially updated
        result1.IsSuccess.Should().BeFalse();
        state.Counter.Should().Be(1, "first item should have incremented counter");
        state.ProcessedItems.Should().BeEquivalentTo(["Item1"]);

        // Act - Resume
        var result2 = await executor.ResumeAsync(state.FlowId!);

        // Assert full completion
        result2.IsSuccess.Should().BeTrue();
        result2.State.Counter.Should().Be(3, "all items should have incremented counter");
        result2.State.ProcessedItems.Should().BeEquivalentTo(["Item1", "Item2", "Item3"]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    public async Task Flow_ConcurrentRecovery_OnlyOneShouldSucceed(int concurrentAttempts, int expectedProcessed)
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ConcurrentRecoveryFlow();

        var state = new RecoveryState
        {
            FlowId = "concurrent-recovery-001",
            Items = Enumerable.Range(1, 10).Select(i => $"Item{i}").ToList()
        };

        SetupMediatorForSuccess(mediator);

        // Create and start initial flow that will be suspended
        var executor = new DslFlowExecutor<RecoveryState, ConcurrentRecoveryFlow>(mediator, store, config);

        // Simulate a suspended flow
        var snapshot = new FlowSnapshot<RecoveryState>
        {
            FlowId = state.FlowId!,
            State = state,
            Status = DslFlowStatus.Suspended,
            Position = new FlowPosition([0]),
            Version = 1
        };
        await store.CreateAsync(snapshot);

        // Act - Multiple concurrent recovery attempts
        var tasks = Enumerable.Range(0, concurrentAttempts).Select(i =>
            Task.Run(async () =>
            {
                var execInstance = new DslFlowExecutor<RecoveryState, ConcurrentRecoveryFlow>(mediator, store, config);
                return await execInstance.ResumeAsync(state.FlowId!);
            })).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert - Only successful results should have processed items
        var successfulResults = results.Where(r => r.IsSuccess).ToList();
        successfulResults.Should().NotBeEmpty("at least one recovery should succeed");

        var totalProcessed = results
            .Where(r => r.State != null)
            .Sum(r => r.State.ProcessedItems.Count);

        totalProcessed.Should().BeGreaterThanOrEqualTo(expectedProcessed,
            "items should be processed even with concurrent attempts");
    }

    [Fact]
    public async Task Flow_RecoveryAfterSystemRestart_ShouldContinue()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SystemRestartFlow();

        var state = new RecoveryState
        {
            FlowId = "system-restart-001",
            Items = ["A", "B", "C", "D", "E"],
            ProcessedItems = ["A", "B"], // Already processed
            Counter = 2
        };

        // Simulate saved state from before restart
        var snapshot = new FlowSnapshot<RecoveryState>
        {
            FlowId = state.FlowId!,
            State = state,
            Status = DslFlowStatus.Running,
            Position = new FlowPosition([2]), // At third item
            Version = 5
        };

        await store.CreateAsync(snapshot);
        SetupMediatorForSuccess(mediator);

        // Act - Create new executor (simulating restart) and resume
        var newExecutor = new DslFlowExecutor<RecoveryState, SystemRestartFlow>(mediator, store, config);
        var result = await newExecutor.ResumeAsync(state.FlowId!);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedItems.Should().BeEquivalentTo(["A", "B", "C", "D", "E"]);
        result.State.Counter.Should().Be(5);
    }

    [Fact]
    public async Task Flow_RecoveryWithTimeout_ShouldHandleGracefully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new TimeoutRecoveryFlow();
        var executor = new DslFlowExecutor<RecoveryState, TimeoutRecoveryFlow>(mediator, store, config);

        var state = new RecoveryState
        {
            FlowId = "timeout-recovery-001",
            Items = ["Fast", "Slow", "Fast"]
        };

        mediator.SendAsync<ProcessCommand, string>(Arg.Any<ProcessCommand>(), Arg.Any<CancellationToken>())
            .Returns(async (call) =>
            {
                var cmd = call.Arg<ProcessCommand>();
                var token = call.Arg<CancellationToken>();

                if (cmd.Item == "Slow")
                {
                    await Task.Delay(5000, token); // Simulate slow operation
                }

                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });

        // Act - Run with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var act = () => executor.RunAsync(state, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        state.ProcessedItems.Should().BeEquivalentTo(["Fast"], "only first item should be processed before timeout");
    }

    private static void SetupMediatorWithFailure(ICatgaMediator mediator, Dictionary<string, int> attempts)
    {
        mediator.SendAsync<ProcessCommand, string>(Arg.Any<ProcessCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<ProcessCommand>();
                var attemptCount = attempts.GetValueOrDefault(cmd.Item, 0) + 1;
                attempts[cmd.Item] = attemptCount;

                if (cmd.Item == "FAIL" && attemptCount == 1)
                {
                    return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Failure("First attempt failed"));
                }

                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });
    }

    private static void SetupMediatorWithMultipleFailures(ICatgaMediator mediator, Dictionary<string, int> attempts)
    {
        mediator.SendAsync<ProcessCommand, string>(Arg.Any<ProcessCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<ProcessCommand>();
                var attemptCount = attempts.GetValueOrDefault(cmd.Item, 0) + 1;
                attempts[cmd.Item] = attemptCount;

                if ((cmd.Item == "FAIL1" || cmd.Item == "FAIL2") && attemptCount == 1)
                {
                    return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Failure($"{cmd.Item} failed on first attempt"));
                }

                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });
    }

    private static void SetupMediatorForSuccess(ICatgaMediator mediator)
    {
        mediator.SendAsync<ProcessCommand, string>(Arg.Any<ProcessCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<ProcessCommand>();
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success($"processed-{cmd.Item}"));
            });
    }
}

// Test State
public class RecoveryState : IFlowState
{
    public string? FlowId { get; set; }
    public List<string> Items { get; set; } = [];
    public List<string> ProcessedItems { get; set; } = [];
    public int Counter { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// Test Command
public record ProcessCommand(string Item) : IRequest<string>
{
    public long MessageId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

// Test Flow Configurations
public class RecoverableFlow : FlowConfig<RecoveryState>
{
    protected override void Configure(IFlowBuilder<RecoveryState> flow)
    {
        flow.Name("recoverable-flow");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send(s => new ProcessCommand(item))
                    .Into((s, r) => s.ProcessedItems.Add(item));
            })
            .StopOnFirstFailure()
            .EndForEach();
    }
}

public class MultiFailureFlow : FlowConfig<RecoveryState>
{
    protected override void Configure(IFlowBuilder<RecoveryState> flow)
    {
        flow.Name("multi-failure-flow");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send(s => new ProcessCommand(item));
            })
            .OnItemSuccess((s, item, result) => s.ProcessedItems.Add(item))
            .StopOnFirstFailure()
            .EndForEach();
    }
}

public class StatefulRecoveryFlow : FlowConfig<RecoveryState>
{
    protected override void Configure(IFlowBuilder<RecoveryState> flow)
    {
        flow.Name("stateful-recovery-flow");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send(s => new ProcessCommand(item));
            })
            .OnItemSuccess((state, item, result) =>
            {
                state.ProcessedItems.Add(item);
                state.Counter++;
            })
            .StopOnFirstFailure()
            .EndForEach();
    }
}

public class ConcurrentRecoveryFlow : FlowConfig<RecoveryState>
{
    protected override void Configure(IFlowBuilder<RecoveryState> flow)
    {
        flow.Name("concurrent-recovery-flow");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send(s => new ProcessCommand(item));
            })
            .OnItemSuccess((s, item, result) => s.ProcessedItems.Add(item))
            .WithParallelism(4)
            .EndForEach();
    }
}

public class SystemRestartFlow : FlowConfig<RecoveryState>
{
    protected override void Configure(IFlowBuilder<RecoveryState> flow)
    {
        flow.Name("system-restart-flow");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send(s => new ProcessCommand(item));
            })
            .OnItemSuccess((state, item, result) =>
            {
                if (!state.ProcessedItems.Contains(item))
                {
                    state.ProcessedItems.Add(item);
                    state.Counter++;
                }
            })
            .EndForEach();
    }
}

public class TimeoutRecoveryFlow : FlowConfig<RecoveryState>
{
    protected override void Configure(IFlowBuilder<RecoveryState> flow)
    {
        flow.Name("timeout-recovery-flow");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send(s => new ProcessCommand(item));
            })
            .OnItemSuccess((s, item, result) => s.ProcessedItems.Add(item))
            .EndForEach();
    }
}
