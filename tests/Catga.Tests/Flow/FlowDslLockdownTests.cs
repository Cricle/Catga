using Catga.Abstractions;
using Catga.Core;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using Catga.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

public class FlowDslLockdownTests
{
    private static string WaitKey(string flowId, params int[] path)
        => path.Length <= 1
            ? $"{flowId}-step-{path[0]}"
            : $"{flowId}-step-{path[0]}-path-{string.Join("-", path.Skip(1))}";

    [Fact]
    public async Task RunAsync_IfDelayBranch_SuspendsAtNestedPosition_AndResumeContinuesBranchThenFlow()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("if-delay-schedule"));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, IfDelayBranchFlow>(mediator, store, new IfDelayBranchFlow(), scheduler);
        var state = new LockdownFlowState
        {
            FlowId = "if-delay-lockdown",
            TakeThenBranch = true
        };

        var firstRun = await executor.RunAsync(state);
        var suspendedSnapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);
        var waitCondition = await store.GetWaitConditionAsync(WaitKey(state.FlowId!, 0, 0, 0));

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        suspendedSnapshot.Should().NotBeNull();
        suspendedSnapshot!.Position.Path.Should().Equal(0, 0, 0);
        waitCondition.Should().NotBeNull();

        waitCondition!.CompletedCount = 1;
        await store.UpdateWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        var resumed = await executor.ResumeAsync(state.FlowId!);
        var completedSnapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);

        resumed.IsSuccess.Should().BeTrue();
        resumed.Status.Should().Be(DslFlowStatus.Completed);
        completedSnapshot.Should().NotBeNull();
        completedSnapshot!.Status.Should().Be(DslFlowStatus.Completed);
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "if-branch"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-if"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NestedIfDelayBranch_SuspendsAtDeepPosition_AndResumeContinuesOuterBranch()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("nested-if-delay-schedule"));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, NestedIfDelayBranchFlow>(mediator, store, new NestedIfDelayBranchFlow(), scheduler);
        var state = new LockdownFlowState
        {
            FlowId = "nested-if-delay-lockdown",
            TakeThenBranch = true,
            TakeNestedThenBranch = true
        };

        var firstRun = await executor.RunAsync(state);
        var suspendedSnapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);
        var waitCondition = await store.GetWaitConditionAsync(WaitKey(state.FlowId!, 0, 0, 0, 0, 0));

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        suspendedSnapshot.Should().NotBeNull();
        suspendedSnapshot!.Position.Path.Should().Equal(0, 0, 0, 0, 0);
        waitCondition.Should().NotBeNull();

        waitCondition!.CompletedCount = 1;
        await store.UpdateWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        var resumed = await executor.ResumeAsync(state.FlowId!);

        resumed.IsSuccess.Should().BeTrue();
        resumed.Status.Should().Be(DslFlowStatus.Completed);
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "nested-if-branch"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-nested-if"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-nested-flow"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_IfStoredBranchPath_DoesNotReevaluateCondition()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, IfStoredBranchFlow>(mediator, store, new IfStoredBranchFlow());
        var state = new LockdownFlowState
        {
            FlowId = "if-stored-branch",
            TakeThenBranch = true
        };

        await store.CreateAsync(new FlowSnapshot<LockdownFlowState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0, -1, 0]),
            Status = DslFlowStatus.Running
        });

        var resumed = await executor.ResumeAsync(state.FlowId!);

        resumed.IsSuccess.Should().BeTrue();
        resumed.Status.Should().Be(DslFlowStatus.Completed);
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "if-else"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "if-then"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-if-branch"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_SwitchStoredCasePath_DoesNotReevaluateSelector()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, SwitchStoredBranchFlow>(mediator, store, new SwitchStoredBranchFlow());
        var state = new LockdownFlowState
        {
            FlowId = "switch-stored-branch",
            RouteKey = "alpha"
        };

        await store.CreateAsync(new FlowSnapshot<LockdownFlowState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0, 1, 0]),
            Status = DslFlowStatus.Running
        });

        var resumed = await executor.ResumeAsync(state.FlowId!);

        resumed.IsSuccess.Should().BeTrue();
        resumed.Status.Should().Be(DslFlowStatus.Completed);
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "switch-beta"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "switch-alpha"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "switch-default"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-switch"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_ForEachStoredPositionWithoutCheckpoint_ContinuesRemainingItems()
    {
        var state = new LockdownFlowState
        {
            FlowId = "foreach-resume-position",
            Items = ["a", "b", "c", "d"],
            ProcessedItems = ["a", "b"]
        };
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<ProcessItemLockdownCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.Arg<ProcessItemLockdownCommand>();
                state.ProcessedItems.Add(command.Item);
                return ValueTask.FromResult(CatgaResult.Success());
            });
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ForEachResumeFlow>(mediator, store, new ForEachResumeFlow());

        await store.CreateAsync(new FlowSnapshot<LockdownFlowState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0, 2]),
            Status = DslFlowStatus.Running
        });

        var resumed = await executor.ResumeAsync(state.FlowId!);

        resumed.IsSuccess.Should().BeTrue();
        state.ProcessedItems.Should().Equal("a", "b", "c", "d");
        await mediator.Received(1).SendAsync(
            Arg.Is<ProcessItemLockdownCommand>(cmd => cmd.Item == "c"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<ProcessItemLockdownCommand>(cmd => cmd.Item == "d"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<ProcessItemLockdownCommand>(cmd => cmd.Item == "a"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<ProcessItemLockdownCommand>(cmd => cmd.Item == "b"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-foreach"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_ForEachSavedProgress_ClearsCheckpointOnCompletion()
    {
        var state = new LockdownFlowState
        {
            FlowId = "foreach-resume-progress",
            Items = ["a", "b", "c", "d"],
            ProcessedItems = ["a", "b"]
        };
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<ProcessItemLockdownCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.Arg<ProcessItemLockdownCommand>();
                state.ProcessedItems.Add(command.Item);
                return ValueTask.FromResult(CatgaResult.Success());
            });
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ForEachResumeFlow>(mediator, store, new ForEachResumeFlow());

        await store.CreateAsync(new FlowSnapshot<LockdownFlowState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0, 2]),
            Status = DslFlowStatus.Running
        });
        await store.SaveForEachProgressAsync(state.FlowId!, 0, new ForEachProgress
        {
            CurrentIndex = 2,
            TotalCount = 4,
            CompletedIndices = [0, 1]
        });

        var resumed = await executor.ResumeAsync(state.FlowId!);
        var remainingProgress = await store.GetForEachProgressAsync(state.FlowId!, 0);

        resumed.IsSuccess.Should().BeTrue();
        remainingProgress.Should().BeNull();
    }

    [Fact]
    public async Task ResumeAsync_ForEachTopLevelSavedProgress_ContinuesWithoutNestedPosition()
    {
        var state = new LockdownFlowState
        {
            FlowId = "foreach-top-level-progress",
            Items = ["a", "b", "c", "d"],
            ProcessedItems = ["a", "b"]
        };
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<ProcessItemLockdownCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.Arg<ProcessItemLockdownCommand>();
                state.ProcessedItems.Add(command.Item);
                return ValueTask.FromResult(CatgaResult.Success());
            });
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ForEachResumeFlow>(mediator, store, new ForEachResumeFlow());

        await store.CreateAsync(new FlowSnapshot<LockdownFlowState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0]),
            Status = DslFlowStatus.Running
        });
        await store.SaveForEachProgressAsync(state.FlowId!, 0, new ForEachProgress
        {
            CurrentIndex = 2,
            TotalCount = 4,
            CompletedIndices = [0, 1]
        });

        var resumed = await executor.ResumeAsync(state.FlowId!);

        resumed.IsSuccess.Should().BeTrue();
        state.ProcessedItems.Should().Equal("a", "b", "c", "d");
        await mediator.Received(1).SendAsync(
            Arg.Is<ProcessItemLockdownCommand>(cmd => cmd.Item == "c"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<ProcessItemLockdownCommand>(cmd => cmd.Item == "d"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<ProcessItemLockdownCommand>(cmd => cmd.Item == "a"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<ProcessItemLockdownCommand>(cmd => cmd.Item == "b"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ForEachFailure_PersistsProgressForTopLevelResume()
    {
        var state = new LockdownFlowState
        {
            FlowId = "foreach-progress-persist",
            Items = ["a", "b", "c", "d"]
        };
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<ProcessItemLockdownCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.Arg<ProcessItemLockdownCommand>();
                if (command.Item == "c")
                    return ValueTask.FromResult(CatgaResult.Failure("item-c failed"));

                state.ProcessedItems.Add(command.Item);
                return ValueTask.FromResult(CatgaResult.Success());
            });

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ForEachResumeFlow>(mediator, store, new ForEachResumeFlow());

        var result = await executor.RunAsync(state);
        var progress = await store.GetForEachProgressAsync(state.FlowId!, 0);
        var snapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("item-c failed");
        progress.Should().NotBeNull();
        progress!.CurrentIndex.Should().Be(2);
        progress.CompletedIndices.Should().Equal(0, 1);
        progress.FailedIndices.Should().Equal(2);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_ForEachSuspendingItemStep_FailsWithClearMessage()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ForEachDelayUnsupportedFlow>(mediator, store, new ForEachDelayUnsupportedFlow());
        var state = new LockdownFlowState
        {
            FlowId = "foreach-delay-unsupported",
            Items = ["a"]
        };

        var result = await executor.RunAsync(state);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("ForEach failed on item 0: ForEach does not support suspending nested steps. Found Delay.");
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-foreach-delay"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ParallelSingleDelayBranch_SuspendsPersistsProgress_AndResumeContinuesOnlyIncompleteBranch()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("parallel-delay-schedule"));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ParallelDelayResumeFlow>(mediator, store, new ParallelDelayResumeFlow(), scheduler);
        var state = new LockdownFlowState { FlowId = "parallel-delay-resume" };

        var firstRun = await executor.RunAsync(state);
        var progress = await store.GetParallelProgressAsync(state.FlowId!, 0);
        var waitCondition = await store.GetWaitConditionAsync(WaitKey(state.FlowId!, 0, 0, 0));
        var suspendedSnapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        progress.Should().NotBeNull();
        progress!.BranchCount.Should().Be(2);
        progress.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 0 &&
            branch.Status == ParallelBranchStatus.Suspended &&
            branch.Position != null &&
            branch.Position.Path.SequenceEqual(new[] { 0, 0, 0 }));
        progress.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 1 &&
            branch.Status == ParallelBranchStatus.Completed);
        waitCondition.Should().NotBeNull();
        suspendedSnapshot.Should().NotBeNull();
        suspendedSnapshot!.Status.Should().Be(DslFlowStatus.Suspended);
        suspendedSnapshot.Position.Path.Should().Equal(0);
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-immediate"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-delayed"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-parallel"),
            Arg.Any<CancellationToken>());

        waitCondition!.CompletedCount = 1;
        await store.UpdateWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        var resumed = await executor.ResumeAsync(state.FlowId!);
        var remainingProgress = await store.GetParallelProgressAsync(state.FlowId!, 0);
        var completedSnapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);

        resumed.IsSuccess.Should().BeTrue();
        resumed.Status.Should().Be(DslFlowStatus.Completed);
        remainingProgress.Should().BeNull();
        completedSnapshot.Should().NotBeNull();
        completedSnapshot!.Status.Should().Be(DslFlowStatus.Completed);
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-immediate"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-delayed"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-parallel"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_ParallelSavedProgress_ContinuesSuspendedBranchWithoutRerunningCompletedBranch()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ParallelDelayResumeFlow>(mediator, store, new ParallelDelayResumeFlow());
        var state = new LockdownFlowState { FlowId = "parallel-saved-progress" };

        await store.CreateAsync(new FlowSnapshot<LockdownFlowState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0]),
            Status = DslFlowStatus.Running
        });
        await store.SaveParallelProgressAsync(state.FlowId!, 0, new ParallelProgress
        {
            BranchCount = 2,
            Branches =
            [
                new ParallelBranchProgress
                {
                    BranchIndex = 0,
                    Status = ParallelBranchStatus.Suspended,
                    Position = new FlowPosition([0, 0, 0])
                },
                new ParallelBranchProgress
                {
                    BranchIndex = 1,
                    Status = ParallelBranchStatus.Completed
                }
            ]
        });

        var resumed = await executor.ResumeAsync(state.FlowId!);
        var remainingProgress = await store.GetParallelProgressAsync(state.FlowId!, 0);

        resumed.IsSuccess.Should().BeTrue();
        resumed.Status.Should().Be(DslFlowStatus.Completed);
        remainingProgress.Should().BeNull();
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-immediate"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-delayed"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-parallel"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ParallelDelayBranch_WithAnotherFailedBranch_SuspendsThenFailsWithoutRerunningFailedBranch()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));
        mediator.SendAsync(Arg.Any<FailCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Failure("parallel-branch-fail")));
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("parallel-failed-delay-schedule"));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ParallelDelayWithFailedBranchFlow>(mediator, store, new ParallelDelayWithFailedBranchFlow(), scheduler);
        var state = new LockdownFlowState { FlowId = "parallel-delay-with-failed-branch" };

        var firstRun = await executor.RunAsync(state);
        var progress = await store.GetParallelProgressAsync(state.FlowId!, 0);
        var waitCondition = await store.GetWaitConditionAsync(WaitKey(state.FlowId!, 0, 0, 0));

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        progress.Should().NotBeNull();
        progress!.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 0 &&
            branch.Status == ParallelBranchStatus.Suspended);
        progress.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 1 &&
            branch.Status == ParallelBranchStatus.Failed &&
            branch.Error == "parallel-branch-fail");

        waitCondition!.CompletedCount = 1;
        await store.UpdateWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        var resumed = await executor.ResumeAsync(state.FlowId!);
        var remainingProgress = await store.GetParallelProgressAsync(state.FlowId!, 0);
        var finalSnapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);

        resumed.IsSuccess.Should().BeFalse();
        resumed.Error.Should().Be("parallel-branch-fail");
        remainingProgress.Should().BeNull();
        finalSnapshot.Should().NotBeNull();
        finalSnapshot!.Status.Should().Be(DslFlowStatus.Failed);
        finalSnapshot.Error.Should().Be("parallel-branch-fail");
        await mediator.Received(1).SendAsync(
            Arg.Is<FailCommand>(cmd => cmd.Name == "parallel-branch-fail"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-delayed-failed"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-parallel-failed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ParallelMultipleDelayBranches_ResumeOneAtATime_CompletesWithoutRerunningFinishedBranch()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));
        var scheduler = Substitute.For<IFlowScheduler>();
        var scheduleCount = 0;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult($"parallel-multi-delay-schedule-{++scheduleCount}"));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ParallelTwoDelayResumeFlow>(mediator, store, new ParallelTwoDelayResumeFlow(), scheduler);
        var state = new LockdownFlowState { FlowId = "parallel-two-delay-resume" };

        var firstRun = await executor.RunAsync(state);
        var progress = await store.GetParallelProgressAsync(state.FlowId!, 0);
        var firstWait = await store.GetWaitConditionAsync(WaitKey(state.FlowId!, 0, 0, 0));
        var secondWait = await store.GetWaitConditionAsync(WaitKey(state.FlowId!, 0, 1, 0));

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        scheduleCount.Should().Be(2);
        progress.Should().NotBeNull();
        progress!.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 0 &&
            branch.Status == ParallelBranchStatus.Suspended &&
            branch.Position != null &&
            branch.Position.Path.SequenceEqual(new[] { 0, 0, 0 }));
        progress.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 1 &&
            branch.Status == ParallelBranchStatus.Suspended &&
            branch.Position != null &&
            branch.Position.Path.SequenceEqual(new[] { 0, 1, 0 }));
        firstWait.Should().NotBeNull();
        secondWait.Should().NotBeNull();

        firstWait!.CompletedCount = 1;
        await store.UpdateWaitConditionAsync(firstWait.CorrelationId, firstWait);

        var firstResume = await executor.ResumeAsync(state.FlowId!);
        var midProgress = await store.GetParallelProgressAsync(state.FlowId!, 0);

        firstResume.IsSuccess.Should().BeTrue();
        firstResume.Status.Should().Be(DslFlowStatus.Suspended);
        midProgress.Should().NotBeNull();
        midProgress!.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 0 &&
            branch.Status == ParallelBranchStatus.Completed);
        midProgress.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 1 &&
            branch.Status == ParallelBranchStatus.Suspended);
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-left"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-right"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-parallel-both"),
            Arg.Any<CancellationToken>());

        secondWait!.CompletedCount = 1;
        await store.UpdateWaitConditionAsync(secondWait.CorrelationId, secondWait);

        var secondResume = await executor.ResumeAsync(state.FlowId!);
        var finalProgress = await store.GetParallelProgressAsync(state.FlowId!, 0);

        secondResume.IsSuccess.Should().BeTrue();
        secondResume.Status.Should().Be(DslFlowStatus.Completed);
        finalProgress.Should().BeNull();
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-left"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "parallel-right"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-parallel-both"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ThrottleDelayAtSecondTopLevelStep_SuspendsWithTopLevelWaitAndResumeContinues()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("throttle-delay-schedule"));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ThrottleDelayResumeFlow>(mediator, store, new ThrottleDelayResumeFlow(), scheduler);
        var state = new LockdownFlowState { FlowId = "throttle-delay-lockdown" };

        var firstRun = await executor.RunAsync(state);
        var suspendedSnapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);
        var waitCondition = await store.GetWaitConditionAsync(WaitKey(state.FlowId!, 1, 0));

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        suspendedSnapshot.Should().NotBeNull();
        suspendedSnapshot!.Position.Path.Should().Equal(1, 0);
        waitCondition.Should().NotBeNull();
        waitCondition!.Step.Should().Be(1);

        waitCondition.CompletedCount = 1;
        await store.UpdateWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        var resumed = await executor.ResumeAsync(state.FlowId!);

        resumed.IsSuccess.Should().BeTrue();
        resumed.Status.Should().Be(DslFlowStatus.Completed);
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "before-throttle"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "inside-throttle"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-throttle"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_ThrottleStoredInnerPosition_ContinuesRemainingInnerStepsWithoutRestart()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, ThrottleResumeFlow>(mediator, store, new ThrottleResumeFlow());
        var state = new LockdownFlowState { FlowId = "throttle-stored-position" };

        await store.CreateAsync(new FlowSnapshot<LockdownFlowState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0, 1]),
            Status = DslFlowStatus.Running
        });

        var resumed = await executor.ResumeAsync(state.FlowId!);

        resumed.IsSuccess.Should().BeTrue();
        resumed.Status.Should().Be(DslFlowStatus.Completed);
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "throttle-1"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "throttle-2"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "throttle-3"),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-throttle-seq"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_WhenAnyWithResult_FirstSuccess_SetsStateAndCompletes()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, WhenAnyWinnerFlow>(mediator, store, new WhenAnyWinnerFlow());
        var state = new LockdownFlowState { FlowId = "when-any-success" };

        var initial = await executor.RunAsync(state);
        var waitCondition = await store.GetWaitConditionAsync($"{state.FlowId}-step-0");
        waitCondition.Should().NotBeNull();

        waitCondition!.CompletedCount = 1;
        waitCondition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "child-a",
            ParentCorrelationId = waitCondition.CorrelationId,
            Success = true,
            Result = "winner-a"
        });
        await store.UpdateWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        var resumed = await executor.ResumeAsync(state.FlowId!);
        var snapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);

        initial.Status.Should().Be(DslFlowStatus.Suspended);
        resumed.IsSuccess.Should().BeTrue();
        resumed.Status.Should().Be(DslFlowStatus.Completed);
        snapshot.Should().NotBeNull();
        snapshot!.State.Winner.Should().Be("winner-a");
        snapshot.Status.Should().Be(DslFlowStatus.Completed);
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-when-any"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_WhenAnyAllChildrenFail_ReturnsFailedAndPreservesLastError()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, WhenAnyWinnerFlow>(mediator, store, new WhenAnyWinnerFlow());
        var state = new LockdownFlowState { FlowId = "when-any-fail" };

        await executor.RunAsync(state);
        var waitCondition = await store.GetWaitConditionAsync($"{state.FlowId}-step-0");
        waitCondition.Should().NotBeNull();

        waitCondition!.CompletedCount = 2;
        waitCondition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "child-a",
            ParentCorrelationId = waitCondition.CorrelationId,
            Success = false,
            Error = "first failure"
        });
        waitCondition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "child-b",
            ParentCorrelationId = waitCondition.CorrelationId,
            Success = false,
            Error = "last failure"
        });
        await store.UpdateWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        var resumed = await executor.ResumeAsync(state.FlowId!);
        var snapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);

        resumed.IsSuccess.Should().BeFalse();
        resumed.Status.Should().Be(DslFlowStatus.Failed);
        resumed.Error.Should().Be("last failure");
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Failed);
        snapshot.Error.Should().Be("last failure");
        state.Winner.Should().BeNull();
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-when-any"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResumeAsync_WhenAllChildFails_ExecutesCompensationAndFailsFlow()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));
        mediator.SendAsync(Arg.Is<IRequest>(request => request.GetType() == typeof(RollbackCommand)), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, WhenAllCompensationFlow>(mediator, store, new WhenAllCompensationFlow());
        var state = new LockdownFlowState { FlowId = "when-all-fail" };

        await executor.RunAsync(state);
        var waitCondition = await store.GetWaitConditionAsync($"{state.FlowId}-step-0");
        waitCondition.Should().NotBeNull();

        waitCondition!.CompletedCount = 2;
        waitCondition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "child-a",
            ParentCorrelationId = waitCondition.CorrelationId,
            Success = true
        });
        waitCondition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "child-b",
            ParentCorrelationId = waitCondition.CorrelationId,
            Success = false,
            Error = "child-b failed"
        });
        await store.UpdateWaitConditionAsync(waitCondition.CorrelationId, waitCondition);

        var resumed = await executor.ResumeAsync(state.FlowId!);
        var snapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);

        resumed.IsSuccess.Should().BeFalse();
        resumed.Status.Should().Be(DslFlowStatus.Failed);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Failed);
        snapshot.Error.Should().Be("child-b failed");
        await mediator.Received(1).SendAsync(
            Arg.Is<IRequest>(request => request.GetType() == typeof(RollbackCommand) && ((RollbackCommand)request).Reason == "rollback-all"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-when-all"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SuccessHooks_PublishStepAndFlowCompletedEvents()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<SuccessCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var published = new List<string>();
        mediator.PublishAsync(Arg.Any<IEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<IEvent>() is HookEvent hook)
                    published.Add(hook.Name);
                return Task.CompletedTask;
            });

        var store = new InMemoryDslFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, HookedSuccessFlow>(mediator, store, new HookedSuccessFlow());

        var result = await executor.RunAsync(new LockdownFlowState { FlowId = "hooked-success" });

        result.IsSuccess.Should().BeTrue();
        published.Should().Equal("step:0", "flow:completed");
    }

    [Fact]
    public async Task RunAsync_FailureHooks_PublishStepAndFlowFailedEvents()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<FailCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Failure("command failed")));

        var published = new List<string>();
        mediator.PublishAsync(Arg.Any<IEvent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<IEvent>() is HookEvent hook)
                    published.Add(hook.Name);
                return Task.CompletedTask;
            });

        var store = new InMemoryDslFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, HookedFailureFlow>(mediator, store, new HookedFailureFlow());

        var result = await executor.RunAsync(new LockdownFlowState { FlowId = "hooked-failure" });

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(DslFlowStatus.Failed);
        published.Should().Equal("step:0:command failed", "flow:command failed");
    }

    [Fact]
    public async Task RunAsync_PersistTaggedCheckpoint_CreatesIntermediateSnapshotVersion()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<CheckpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));
        mediator.SendAsync(Arg.Any<FailCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Failure("checkpoint failure")));

        var store = new StrictOptimisticDslFlowStore();
        var executor = new DslFlowExecutor<LockdownFlowState, PersistCheckpointFlow>(mediator, store, new PersistCheckpointFlow());
        var state = new LockdownFlowState { FlowId = "persist-flow" };

        var result = await executor.RunAsync(state);
        var snapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);

        result.IsSuccess.Should().BeFalse();
        store.RejectedUpdateCount.Should().Be(0);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Failed);
        snapshot.Error.Should().Be("checkpoint failure");
        snapshot.Version.Should().Be(2);
        snapshot.Position.CurrentIndex.Should().Be(1);
    }

    [Fact]
    public async Task DefaultFlowResumeHandler_ForScheduleAtStep_CompletesSuspendedFlow()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.SendAsync(Arg.Any<AfterResumeCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(CatgaResult.Success()));

        var store = TestStoreExtensions.CreateTestFlowStore();
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("schedule-lockdown"));

        var services = new ServiceCollection();
        services.AddSingleton(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddSingleton(scheduler);
        services.AddSingleton(Substitute.For<ILogger<DefaultFlowResumeHandler>>());
        services.AddFlow<LockdownFlowState, ScheduleAtResumeFlow>();
        services.AddFlowResumeHandler();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IFlow<LockdownFlowState>>();
        var state = new LockdownFlowState
        {
            FlowId = "schedule-flow",
            ResumeAtUtc = DateTime.UtcNow.AddHours(2)
        };

        var firstRun = await executor.RunAsync(state);
        var resumeHandler = provider.GetRequiredService<IFlowResumeHandler>();
        await resumeHandler.ResumeFlowAsync(state.FlowId!, state.FlowId!);

        var snapshot = await store.GetAsync<LockdownFlowState>(state.FlowId!);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Completed);
        await mediator.Received(1).SendAsync(
            Arg.Is<AfterResumeCommand>(cmd => cmd.Name == "after-schedule"),
            Arg.Any<CancellationToken>());
    }
}

public class LockdownFlowState : IFlowState
{
    private int _changedMask;

    public string? FlowId { get; set; }
    public string? Winner { get; set; }
    public bool TakeThenBranch { get; set; }
    public bool TakeNestedThenBranch { get; set; }
    public string RouteKey { get; set; } = "alpha";
    public List<string> Items { get; set; } = [];
    public List<string> ProcessedItems { get; set; } = [];
    public DateTime ResumeAtUtc { get; set; } = DateTime.UtcNow.AddHours(1);

    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() => [];
}

public record ChildQueryCommand(string Name) : IRequest<string>
{
    public long MessageId { get; init; }
}

public record ChildKickoffCommand(string Name) : IRequest
{
    public long MessageId { get; init; }
}

public record AfterResumeCommand(string Name) : IRequest
{
    public long MessageId { get; init; }
}

public record SuccessCommand(string Name) : IRequest
{
    public long MessageId { get; init; }
}

public record CheckpointCommand(string Name) : IRequest
{
    public long MessageId { get; init; }
}

public record FailCommand(string Name) : IRequest
{
    public long MessageId { get; init; }
}

public record RollbackCommand(string Reason) : IRequest
{
    public long MessageId { get; init; }
}

public record ProcessItemLockdownCommand(string Item) : IRequest
{
    public long MessageId { get; init; }
}

public record HookEvent(string Name) : IEvent
{
    public long MessageId { get; init; }
}

public class WhenAnyWinnerFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.WhenAny<LockdownFlowState, string>(
                state => new ChildQueryCommand("primary"),
                state => new ChildQueryCommand("secondary"))
            .Into((state, winner) => state.Winner = winner);

        flow.Send(state => new AfterResumeCommand("after-when-any"));
    }
}

public class WhenAllCompensationFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.WhenAll(
                state => new ChildKickoffCommand("child-a"),
                state => new ChildKickoffCommand("child-b"))
            .IfAnyFail(state => new RollbackCommand("rollback-all"));

        flow.Send(state => new AfterResumeCommand("after-when-all"));
    }
}

public class HookedSuccessFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.OnStepCompleted((state, step) => new HookEvent($"step:{step}"));
        flow.OnFlowCompleted(state => new HookEvent("flow:completed"));
        flow.Send(state => new SuccessCommand("ok"));
    }
}

public class HookedFailureFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.OnStepFailed((state, step, error) => new HookEvent($"step:{step}:{error}"));
        flow.OnFlowFailed((state, error) => new HookEvent($"flow:{error}"));
        flow.Send(state => new FailCommand("boom"));
    }
}

public class PersistCheckpointFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.Persist().ForTags("checkpoint");
        flow.Send(state => new CheckpointCommand("checkpoint")).Tag("checkpoint");
        flow.Send(state => new FailCommand("after-checkpoint"));
    }
}

public class ScheduleAtResumeFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.ScheduleAt(state => state.ResumeAtUtc);
        flow.Send(state => new AfterResumeCommand("after-schedule"));
    }
}

public class IfDelayBranchFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.If(state => state.TakeThenBranch)
            .Delay(TimeSpan.FromMinutes(5))
            .Send(state => new AfterResumeCommand("if-branch"))
        .Else()
            .Send(state => new AfterResumeCommand("if-else"))
        .EndIf();

        flow.Send(state => new AfterResumeCommand("after-if"));
    }
}

public class IfStoredBranchFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.If(state => state.TakeThenBranch)
            .Send(state => new AfterResumeCommand("if-then"))
        .Else()
            .Send(state => new AfterResumeCommand("if-else"))
        .EndIf();

        flow.Send(state => new AfterResumeCommand("after-if-branch"));
    }
}

public class SwitchStoredBranchFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.Switch(state => state.RouteKey)
            .Case("alpha", branch => branch.Send(state => new AfterResumeCommand("switch-alpha")))
            .Case("beta", branch => branch.Send(state => new AfterResumeCommand("switch-beta")))
            .Default(branch => branch.Send(state => new AfterResumeCommand("switch-default")))
        .EndSwitch();

        flow.Send(state => new AfterResumeCommand("after-switch"));
    }
}

public class NestedIfDelayBranchFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.If(state => state.TakeThenBranch)
            .If(state => state.TakeNestedThenBranch)
                .Delay(TimeSpan.FromMinutes(5))
                .Send(state => new AfterResumeCommand("nested-if-branch"))
            .Else()
                .Send(state => new AfterResumeCommand("nested-if-else"))
            .EndIf()
            .Send(state => new AfterResumeCommand("after-nested-if"))
        .Else()
            .Send(state => new AfterResumeCommand("outer-else"))
        .EndIf();

        flow.Send(state => new AfterResumeCommand("after-nested-flow"));
    }
}

public class ForEachResumeFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.ForEach(state => state.Items)
            .Configure((item, branch) => branch.Send(state => new ProcessItemLockdownCommand(item)))
            .EndForEach();

        flow.Send(state => new AfterResumeCommand("after-foreach"));
    }
}

public class ForEachDelayUnsupportedFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.ForEach(state => state.Items)
            .Configure((item, branch) => branch.Delay(TimeSpan.FromMinutes(5)))
            .EndForEach();

        flow.Send(state => new AfterResumeCommand("after-foreach-delay"));
    }
}

public class ParallelDelayResumeFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.Parallel()
            .Branch(branch =>
            {
                branch.Delay(TimeSpan.FromMinutes(5));
                branch.Send(state => new AfterResumeCommand("parallel-delayed"));
            })
            .Branch(branch => branch.Send(state => new AfterResumeCommand("parallel-immediate")))
        .EndParallel();

        flow.Send(state => new AfterResumeCommand("after-parallel"));
    }
}

public class ParallelDelayWithFailedBranchFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.Parallel()
            .Branch(branch =>
            {
                branch.Delay(TimeSpan.FromMinutes(5));
                branch.Send(state => new AfterResumeCommand("parallel-delayed-failed"));
            })
            .Branch(branch => branch.Send(state => new FailCommand("parallel-branch-fail")))
        .EndParallel();

        flow.Send(state => new AfterResumeCommand("after-parallel-failed"));
    }
}

public class ParallelTwoDelayResumeFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.Parallel()
            .Branch(branch =>
            {
                branch.Delay(TimeSpan.FromMinutes(5));
                branch.Send(state => new AfterResumeCommand("parallel-left"));
            })
            .Branch(branch =>
            {
                branch.Delay(TimeSpan.FromMinutes(10));
                branch.Send(state => new AfterResumeCommand("parallel-right"));
            })
        .EndParallel();

        flow.Send(state => new AfterResumeCommand("after-parallel-both"));
    }
}

public class ThrottleDelayResumeFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.Send(state => new AfterResumeCommand("before-throttle"));

        flow.Throttle(1)
            .Execute(inner =>
            {
                inner.Delay(TimeSpan.FromMinutes(5));
                inner.Send(state => new AfterResumeCommand("inside-throttle"));
            })
        .EndThrottle();

        flow.Send(state => new AfterResumeCommand("after-throttle"));
    }
}

public class ThrottleResumeFlow : FlowConfig<LockdownFlowState>
{
    protected override void Configure(IFlowBuilder<LockdownFlowState> flow)
    {
        flow.Throttle(1)
            .Execute(inner =>
            {
                inner.Send(state => new AfterResumeCommand("throttle-1"));
                inner.Send(state => new AfterResumeCommand("throttle-2"));
                inner.Send(state => new AfterResumeCommand("throttle-3"));
            })
        .EndThrottle();

        flow.Send(state => new AfterResumeCommand("after-throttle-seq"));
    }
}
