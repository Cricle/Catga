using Catga.Abstractions;
using Catga.Core;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.E2E.Tests.FlowSemanticGap;

public class FlowDslLockdownE2ETests
{
    private static string WaitKey(string flowId, params int[] path)
        => path.Length <= 1
            ? $"{flowId}-step-{path[0]}"
            : $"{flowId}-step-{path[0]}-path-{string.Join("-", path.Skip(1))}";

    [Fact]
    public async Task IfDelayWithinBranch_WithRegisteredResumePipeline_CompletesBranchThenFlow()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();
        var scheduler = new LockdownRecordingScheduler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddSingleton<IFlowScheduler>(scheduler);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, IfDelayBranchLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var resumeHandler = provider.GetRequiredService<IFlowResumeHandler>();
        var state = new LockdownE2EState
        {
            FlowId = "if-delay-e2e",
            TakeThenBranch = true
        };

        var firstRun = await flow.RunAsync(state);
        var suspendedSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        await resumeHandler.ResumeFlowAsync(state.FlowId!, state.FlowId!);

        var completedSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        suspendedSnapshot.Should().NotBeNull();
        suspendedSnapshot!.Position.Path.Should().Equal(0, 0, 0);
        completedSnapshot.Should().NotBeNull();
        completedSnapshot!.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "if-branch").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-if").Should().Be(1);
    }

    [Fact]
    public async Task NestedIfDelayWithinBranch_WithRegisteredResumePipeline_CompletesRemainingOuterBranch()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();
        var scheduler = new LockdownRecordingScheduler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddSingleton<IFlowScheduler>(scheduler);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, NestedIfDelayBranchLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var resumeHandler = provider.GetRequiredService<IFlowResumeHandler>();
        var state = new LockdownE2EState
        {
            FlowId = "nested-if-delay-e2e",
            TakeThenBranch = true,
            TakeNestedThenBranch = true
        };

        var firstRun = await flow.RunAsync(state);
        var suspendedSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        await resumeHandler.ResumeFlowAsync(state.FlowId!, state.FlowId!);

        var completedSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        suspendedSnapshot.Should().NotBeNull();
        suspendedSnapshot!.Position.Path.Should().Equal(0, 0, 0, 0, 0);
        completedSnapshot.Should().NotBeNull();
        completedSnapshot!.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "nested-if-branch").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-nested-if").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-nested-flow").Should().Be(1);
    }

    [Fact]
    public async Task ResumeAsync_WithStoredIfElseBranch_UsesPersistedBranchPath()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, IfStoredBranchLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var state = new LockdownE2EState
        {
            FlowId = "if-stored-e2e",
            TakeThenBranch = true
        };

        await store.CreateAsync(new FlowSnapshot<LockdownE2EState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0, -1, 0]),
            Status = DslFlowStatus.Running
        });

        var resumed = await flow.ResumeAsync(state.FlowId!);
        var snapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        resumed.Status.Should().Be(DslFlowStatus.Completed);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "if-else").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "if-then").Should().Be(0);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-if-branch").Should().Be(1);
    }

    [Fact]
    public async Task ResumeAsync_WithStoredSwitchCase_UsesPersistedCasePath()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, SwitchStoredBranchLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var state = new LockdownE2EState
        {
            FlowId = "switch-stored-e2e",
            RouteKey = "alpha"
        };

        await store.CreateAsync(new FlowSnapshot<LockdownE2EState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0, 1, 0]),
            Status = DslFlowStatus.Running
        });

        var resumed = await flow.ResumeAsync(state.FlowId!);
        var snapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        resumed.Status.Should().Be(DslFlowStatus.Completed);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "switch-beta").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "switch-alpha").Should().Be(0);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "switch-default").Should().Be(0);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-switch").Should().Be(1);
    }

    [Fact]
    public async Task ResumeAsync_ForEachStoredPositionWithoutCheckpoint_ContinuesRemainingItems()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ForEachResumeLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var state = new LockdownE2EState
        {
            FlowId = "foreach-position-e2e",
            Items = ["a", "b", "c", "d"],
            ProcessedItems = ["a", "b"]
        };

        await store.CreateAsync(new FlowSnapshot<LockdownE2EState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0, 2]),
            Status = DslFlowStatus.Running
        });

        var resumed = await flow.ResumeAsync(state.FlowId!);
        var snapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        resumed.Status.Should().Be(DslFlowStatus.Completed);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<ProcessItemLockdownCommand>(cmd => cmd.Item == "c").Should().Be(1);
        mediator.Count<ProcessItemLockdownCommand>(cmd => cmd.Item == "d").Should().Be(1);
        mediator.Count<ProcessItemLockdownCommand>(cmd => cmd.Item == "a").Should().Be(0);
        mediator.Count<ProcessItemLockdownCommand>(cmd => cmd.Item == "b").Should().Be(0);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-foreach").Should().Be(1);
    }

    [Fact]
    public async Task ResumeAsync_ForEachSavedProgress_ClearsCheckpointOnCompletion()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ForEachResumeLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var state = new LockdownE2EState
        {
            FlowId = "foreach-progress-e2e",
            Items = ["a", "b", "c", "d"],
            ProcessedItems = ["a", "b"]
        };

        await store.CreateAsync(new FlowSnapshot<LockdownE2EState>
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

        var resumed = await flow.ResumeAsync(state.FlowId!);
        var remainingProgress = await store.GetForEachProgressAsync(state.FlowId!, 0);

        resumed.Status.Should().Be(DslFlowStatus.Completed);
        remainingProgress.Should().BeNull();
    }

    [Fact]
    public async Task ResumeAsync_ForEachTopLevelSavedProgress_ContinuesWithoutNestedPosition()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ForEachResumeLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var state = new LockdownE2EState
        {
            FlowId = "foreach-top-level-progress-e2e",
            Items = ["a", "b", "c", "d"]
        };

        await store.CreateAsync(new FlowSnapshot<LockdownE2EState>
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

        var resumed = await flow.ResumeAsync(state.FlowId!);

        resumed.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<ProcessItemLockdownCommand>(cmd => cmd.Item == "c").Should().Be(1);
        mediator.Count<ProcessItemLockdownCommand>(cmd => cmd.Item == "d").Should().Be(1);
        mediator.Count<ProcessItemLockdownCommand>(cmd => cmd.Item == "a").Should().Be(0);
        mediator.Count<ProcessItemLockdownCommand>(cmd => cmd.Item == "b").Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ForEachFailure_PersistsProgressForTopLevelResume()
    {
        var mediator = new LockdownRecordingMediator();
        mediator.FailRequest<ProcessItemLockdownCommand>(cmd => cmd.Item == "c", "item-c failed");

        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ForEachResumeLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var state = new LockdownE2EState
        {
            FlowId = "foreach-progress-persist-e2e",
            Items = ["a", "b", "c", "d"]
        };

        var result = await flow.RunAsync(state);
        var progress = await store.GetForEachProgressAsync(state.FlowId!, 0);
        var snapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        result.Status.Should().Be(DslFlowStatus.Failed);
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
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ForEachDelayUnsupportedLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var state = new LockdownE2EState
        {
            FlowId = "foreach-delay-unsupported-e2e",
            Items = ["a"]
        };

        var result = await flow.RunAsync(state);

        result.Status.Should().Be(DslFlowStatus.Failed);
        result.Error.Should().Be("ForEach failed on item 0: ForEach does not support suspending nested steps. Found Delay.");
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-foreach-delay").Should().Be(0);
    }

    [Fact]
    public async Task ParallelSingleDelayBranch_WithRegisteredResumePipeline_CompletesWithoutRerunningCompletedBranch()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();
        var scheduler = new LockdownRecordingScheduler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddSingleton<IFlowScheduler>(scheduler);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ParallelDelayResumeLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var resumeHandler = provider.GetRequiredService<IFlowResumeHandler>();
        var state = new LockdownE2EState { FlowId = "parallel-delay-resume-e2e" };

        var firstRun = await flow.RunAsync(state);
        var progress = await store.GetParallelProgressAsync(state.FlowId!, 0);
        var suspendedSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        await resumeHandler.ResumeFlowAsync(state.FlowId!, state.FlowId!);

        var completedSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);
        var remainingProgress = await store.GetParallelProgressAsync(state.FlowId!, 0);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        progress.Should().NotBeNull();
        progress!.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 0 &&
            branch.Status == ParallelBranchStatus.Suspended);
        progress.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 1 &&
            branch.Status == ParallelBranchStatus.Completed);
        suspendedSnapshot.Should().NotBeNull();
        suspendedSnapshot!.Status.Should().Be(DslFlowStatus.Suspended);
        completedSnapshot.Should().NotBeNull();
        completedSnapshot!.Status.Should().Be(DslFlowStatus.Completed);
        remainingProgress.Should().BeNull();
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "parallel-immediate").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "parallel-delayed").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-parallel").Should().Be(1);
    }

    [Fact]
    public async Task ResumeAsync_WithStoredParallelProgress_ContinuesSuspendedBranchWithoutRerunningCompletedBranch()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ParallelDelayResumeLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var state = new LockdownE2EState { FlowId = "parallel-saved-progress-e2e" };

        await store.CreateAsync(new FlowSnapshot<LockdownE2EState>
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

        var resumed = await flow.ResumeAsync(state.FlowId!);
        var remainingProgress = await store.GetParallelProgressAsync(state.FlowId!, 0);

        resumed.Status.Should().Be(DslFlowStatus.Completed);
        remainingProgress.Should().BeNull();
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "parallel-immediate").Should().Be(0);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "parallel-delayed").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-parallel").Should().Be(1);
    }

    [Fact]
    public async Task ParallelDelayBranch_WithAnotherFailedBranch_CompletesPendingBranchThenFails()
    {
        var mediator = new LockdownRecordingMediator();
        mediator.FailRequest<FailFlowCommand>(cmd => cmd.Name == "parallel-branch-fail", "parallel-branch-fail");
        var store = new InMemoryDslFlowStore();
        var scheduler = new LockdownRecordingScheduler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddSingleton<IFlowScheduler>(scheduler);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ParallelDelayFailedBranchLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var resumeHandler = provider.GetRequiredService<IFlowResumeHandler>();
        var state = new LockdownE2EState { FlowId = "parallel-delay-failed-e2e" };

        var firstRun = await flow.RunAsync(state);
        var progress = await store.GetParallelProgressAsync(state.FlowId!, 0);

        await resumeHandler.ResumeFlowAsync(state.FlowId!, state.FlowId!);

        var finalSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);
        var remainingProgress = await store.GetParallelProgressAsync(state.FlowId!, 0);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        progress.Should().NotBeNull();
        progress!.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 0 &&
            branch.Status == ParallelBranchStatus.Suspended);
        progress.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 1 &&
            branch.Status == ParallelBranchStatus.Failed &&
            branch.Error == "parallel-branch-fail");
        finalSnapshot.Should().NotBeNull();
        finalSnapshot!.Status.Should().Be(DslFlowStatus.Failed);
        finalSnapshot.Error.Should().Be("parallel-branch-fail");
        remainingProgress.Should().BeNull();
        mediator.Count<FailFlowCommand>(cmd => cmd.Name == "parallel-branch-fail").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "parallel-delayed-failed").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-parallel-failed").Should().Be(0);
    }

    [Fact]
    public async Task ParallelMultipleDelayBranches_WithBranchAwareResume_CompletesOneBranchAtATime()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();
        var scheduler = new LockdownRecordingScheduler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddSingleton<IFlowScheduler>(scheduler);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ParallelTwoDelayResumeLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var resumeHandler = provider.GetRequiredService<IFlowResumeHandler>();
        var state = new LockdownE2EState { FlowId = "parallel-two-delay-resume-e2e" };

        var firstRun = await flow.RunAsync(state);
        var progress = await store.GetParallelProgressAsync(state.FlowId!, 0);
        var firstWait = await store.GetWaitConditionAsync(WaitKey(state.FlowId!, 0, 0, 0));
        var secondWait = await store.GetWaitConditionAsync(WaitKey(state.FlowId!, 0, 1, 0));

        scheduler.ScheduleCalls.Should().HaveCount(2);
        scheduler.ScheduleCalls.Select(call => call.StateId).Should().OnlyHaveUniqueItems();
        firstWait.Should().NotBeNull();
        secondWait.Should().NotBeNull();

        await resumeHandler.ResumeFlowAsync(state.FlowId!, scheduler.ScheduleCalls[0].StateId);

        var midSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);
        var midProgress = await store.GetParallelProgressAsync(state.FlowId!, 0);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        progress.Should().NotBeNull();
        progress!.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 0 &&
            branch.Status == ParallelBranchStatus.Suspended);
        progress.Branches.Should().ContainSingle(branch =>
            branch.BranchIndex == 1 &&
            branch.Status == ParallelBranchStatus.Suspended);
        midSnapshot.Should().NotBeNull();
        midSnapshot!.Status.Should().Be(DslFlowStatus.Suspended);
        midProgress.Should().NotBeNull();
        midProgress!.Branches.Should().ContainSingle(branch => branch.BranchIndex == 0 && branch.Status == ParallelBranchStatus.Completed);
        midProgress.Branches.Should().ContainSingle(branch => branch.BranchIndex == 1 && branch.Status == ParallelBranchStatus.Suspended);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "parallel-left").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "parallel-right").Should().Be(0);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-parallel-both").Should().Be(0);

        await resumeHandler.ResumeFlowAsync(state.FlowId!, scheduler.ScheduleCalls[1].StateId);

        var finalSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);
        var finalProgress = await store.GetParallelProgressAsync(state.FlowId!, 0);

        finalSnapshot.Should().NotBeNull();
        finalSnapshot!.Status.Should().Be(DslFlowStatus.Completed);
        finalProgress.Should().BeNull();
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "parallel-left").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "parallel-right").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-parallel-both").Should().Be(1);
    }

    [Fact]
    public async Task ThrottleDelayAtSecondTopLevelStep_WithRegisteredResumePipeline_CompletesAfterResume()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();
        var scheduler = new LockdownRecordingScheduler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddSingleton<IFlowScheduler>(scheduler);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ThrottleDelayResumeLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var resumeHandler = provider.GetRequiredService<IFlowResumeHandler>();
        var state = new LockdownE2EState { FlowId = "throttle-delay-e2e" };

        var firstRun = await flow.RunAsync(state);
        var suspendedSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);
        var waitCondition = await store.GetWaitConditionAsync(WaitKey(state.FlowId!, 1, 0));

        await resumeHandler.ResumeFlowAsync(state.FlowId!, state.FlowId!);

        var completedSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        suspendedSnapshot.Should().NotBeNull();
        suspendedSnapshot!.Position.Path.Should().Equal(1, 0);
        waitCondition.Should().NotBeNull();
        waitCondition!.Step.Should().Be(1);
        completedSnapshot.Should().NotBeNull();
        completedSnapshot!.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "before-throttle").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "inside-throttle").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-throttle").Should().Be(1);
    }

    [Fact]
    public async Task ResumeAsync_WithStoredThrottleInnerPosition_ContinuesRemainingInnerStepsWithoutRestart()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ThrottleResumeLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var state = new LockdownE2EState { FlowId = "throttle-stored-e2e" };

        await store.CreateAsync(new FlowSnapshot<LockdownE2EState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = new FlowPosition([0, 1]),
            Status = DslFlowStatus.Running
        });

        var resumed = await flow.ResumeAsync(state.FlowId!);

        resumed.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "throttle-1").Should().Be(0);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "throttle-2").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "throttle-3").Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-throttle-seq").Should().Be(1);
    }

    [Fact]
    public async Task Delay_WithRegisteredResumePipeline_CompletesAfterResume()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();
        var scheduler = new LockdownRecordingScheduler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddSingleton<IFlowScheduler>(scheduler);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, DelayLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var resumeHandler = provider.GetRequiredService<IFlowResumeHandler>();
        var state = new LockdownE2EState { FlowId = "delay-e2e" };

        var firstRun = await flow.RunAsync(state);
        await resumeHandler.ResumeFlowAsync(state.FlowId!, state.FlowId!);

        var snapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        scheduler.ScheduleCalls.Should().HaveCount(1);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<FinalizeFlowCommand>().Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-delay").Should().Be(1);
    }

    [Fact]
    public async Task ScheduleAt_WithRegisteredResumePipeline_CompletesAfterResume()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();
        var scheduler = new LockdownRecordingScheduler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddSingleton<IFlowScheduler>(scheduler);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, ScheduleAtLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var resumeHandler = provider.GetRequiredService<IFlowResumeHandler>();
        var state = new LockdownE2EState
        {
            FlowId = "schedule-e2e",
            ResumeAtUtc = DateTime.UtcNow.AddHours(3)
        };

        var firstRun = await flow.RunAsync(state);
        await resumeHandler.ResumeFlowAsync(state.FlowId!, state.FlowId!);

        var snapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        scheduler.ScheduleCalls.Should().HaveCount(1);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Completed);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-schedule").Should().Be(1);
    }

    [Fact]
    public async Task WhenAny_FirstSuccessViaFlowCompletedEvent_AutoResumesAndAppliesWinner()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, WhenAnyWinnerLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var completionHandler = provider.GetRequiredService<IEventHandler<FlowCompletedEvent>>();
        var state = new LockdownE2EState { FlowId = "when-any-e2e" };

        var firstRun = await flow.RunAsync(state);
        await completionHandler.HandleAsync(new FlowCompletedEvent(
            "child-a",
            $"{state.FlowId}-step-0",
            true,
            null,
            "winner-a"));

        var snapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);
        var waitCondition = await store.GetWaitConditionAsync($"{state.FlowId}-step-0");

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Completed);
        snapshot.State.Winner.Should().Be("winner-a");
        waitCondition.Should().BeNull();
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-when-any").Should().Be(1);
    }

    [Fact]
    public async Task WhenAll_FailedChildViaFlowCompletedEvent_CompensatesAndFails()
    {
        var mediator = new LockdownRecordingMediator();
        var store = new InMemoryDslFlowStore();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICatgaMediator>(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddDslFlow();
        services.AddFlow<LockdownE2EState, WhenAllCompensationLockdownFlow>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var flow = scope.ServiceProvider.GetRequiredService<IFlow<LockdownE2EState>>();
        var completionHandler = provider.GetRequiredService<IEventHandler<FlowCompletedEvent>>();
        var state = new LockdownE2EState { FlowId = "when-all-e2e" };

        var firstRun = await flow.RunAsync(state);

        await completionHandler.HandleAsync(new FlowCompletedEvent(
            "child-a",
            $"{state.FlowId}-step-0",
            true,
            null,
            null));

        var snapshotAfterFirst = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        await completionHandler.HandleAsync(new FlowCompletedEvent(
            "child-b",
            $"{state.FlowId}-step-0",
            false,
            "child-b failed",
            null));

        var finalSnapshot = await store.GetAsync<LockdownE2EState>(state.FlowId!);

        firstRun.Status.Should().Be(DslFlowStatus.Suspended);
        snapshotAfterFirst.Should().NotBeNull();
        snapshotAfterFirst!.Status.Should().Be(DslFlowStatus.Suspended);
        finalSnapshot.Should().NotBeNull();
        finalSnapshot!.Status.Should().Be(DslFlowStatus.Failed);
        finalSnapshot.Error.Should().Be("child-b failed");
        mediator.Count<RollbackFlowCommand>().Should().Be(1);
        mediator.Count<FinalizeFlowCommand>(cmd => cmd.Name == "after-when-all").Should().Be(0);
    }

    private sealed class LockdownRecordingMediator : ICatgaMediator
    {
        private sealed record RequestFailure(Type RequestType, Func<object, bool> Predicate, string Error);

        private readonly List<object> _requests = [];
        private readonly List<object> _events = [];
        private readonly List<RequestFailure> _requestFailures = [];

        public int Count<TRequest>() => _requests.Count(request => request is TRequest);

        public int Count<TRequest>(Func<TRequest, bool> predicate) => _requests.OfType<TRequest>().Count(predicate);

        public void FailRequest<TRequest>(Func<TRequest, bool> predicate, string error)
            where TRequest : IRequest
        {
            _requestFailures.Add(new RequestFailure(typeof(TRequest), request => predicate((TRequest)request), error));
        }

        public ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>
        {
            _requests.Add(request!);
            var failure = FindFailure(request!);
            if (failure != null)
                return ValueTask.FromResult(CatgaResult<TResponse>.Failure(failure.Error));
            return ValueTask.FromResult(CatgaResult<TResponse>.Success(CreateResponse<TResponse>()));
        }

        public ValueTask<CatgaResult> SendAsync<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _requests.Add(request!);
            var failure = FindFailure(request!);
            if (failure != null)
                return ValueTask.FromResult(CatgaResult.Failure(failure.Error));
            return ValueTask.FromResult(CatgaResult.Success());
        }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            _events.Add(@event!);
            return Task.CompletedTask;
        }

        public ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<TRequest, TResponse>(
            IReadOnlyList<TRequest> requests,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>
        {
            foreach (var request in requests)
                _requests.Add(request!);

            IReadOnlyList<CatgaResult<TResponse>> results = requests
                .Select(_ => CatgaResult<TResponse>.Success(CreateResponse<TResponse>()))
                .ToList();

            return ValueTask.FromResult(results);
        }

        public async IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<TRequest, TResponse>(
            IAsyncEnumerable<TRequest> requests,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>
        {
            await foreach (var request in requests.WithCancellation(cancellationToken))
            {
                _requests.Add(request!);
                yield return CatgaResult<TResponse>.Success(CreateResponse<TResponse>());
            }
        }

        public Task PublishBatchAsync<TEvent>(IReadOnlyList<TEvent> events, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            foreach (var @event in events)
                _events.Add(@event!);

            return Task.CompletedTask;
        }

        private static TResponse CreateResponse<TResponse>()
        {
            if (typeof(TResponse) == typeof(string))
                return (TResponse)(object)"ok";

            if (typeof(TResponse) == typeof(bool))
                return (TResponse)(object)true;

            return Activator.CreateInstance<TResponse>();
        }

        private RequestFailure? FindFailure(object request)
            => _requestFailures.FirstOrDefault(failure =>
                failure.RequestType.IsInstanceOfType(request) &&
                failure.Predicate(request));
    }

    private sealed class LockdownRecordingScheduler : IFlowScheduler
    {
        public List<(string FlowId, string StateId, DateTimeOffset ResumeAt)> ScheduleCalls { get; } = [];

        public ValueTask<string> ScheduleResumeAsync(
            string flowId,
            string stateId,
            DateTimeOffset resumeAt,
            CancellationToken ct = default)
        {
            ScheduleCalls.Add((flowId, stateId, resumeAt));
            return ValueTask.FromResult($"schedule-{ScheduleCalls.Count}");
        }

        public ValueTask<bool> CancelScheduledResumeAsync(string scheduleId, CancellationToken ct = default)
            => ValueTask.FromResult(true);
    }

    private sealed class LockdownE2EState : IFlowState
    {
        public string? FlowId { get; set; } = Guid.NewGuid().ToString("N");
        public string? Winner { get; set; }
        public bool TakeThenBranch { get; set; }
        public bool TakeNestedThenBranch { get; set; }
        public string RouteKey { get; set; } = "alpha";
        public List<string> Items { get; set; } = [];
        public List<string> ProcessedItems { get; set; } = [];
        public DateTime ResumeAtUtc { get; set; } = DateTime.UtcNow.AddHours(1);
        public bool HasChanges => true;
        public int GetChangedMask() => 0;
        public bool IsFieldChanged(int fieldIndex) => false;
        public void ClearChanges() { }
        public void MarkChanged(int fieldIndex) { }
        public IEnumerable<string> GetChangedFieldNames() => [];
    }

    private sealed record KickoffChildWorkCommand(string FlowId, string ChildId) : IRequest
    {
        public long MessageId => 0;
    }

    private sealed record KickoffChildResultCommand(string FlowId, string ChildId) : IRequest<string>
    {
        public long MessageId => 0;
    }

    private sealed record FinalizeFlowCommand(string Name) : IRequest
    {
        public long MessageId => 0;
    }

    private sealed record FailFlowCommand(string Name) : IRequest
    {
        public long MessageId => 0;
    }

    private sealed record RollbackFlowCommand(string FlowId) : IRequest
    {
        public long MessageId => 0;
    }

    private sealed record ProcessItemLockdownCommand(string Item) : IRequest
    {
        public long MessageId => 0;
    }

    private sealed class DelayLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.Delay(TimeSpan.FromMinutes(5));
            flow.Send(state => new FinalizeFlowCommand("after-delay"));
        }
    }

    private sealed class ScheduleAtLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.ScheduleAt(state => state.ResumeAtUtc);
            flow.Send(state => new FinalizeFlowCommand("after-schedule"));
        }
    }

    private sealed class WhenAnyWinnerLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.WhenAny<LockdownE2EState, string>(
                    state => new KickoffChildResultCommand(state.FlowId!, "child-a"),
                    state => new KickoffChildResultCommand(state.FlowId!, "child-b"))
                .Into((state, winner) => state.Winner = winner);

            flow.Send(state => new FinalizeFlowCommand("after-when-any"));
        }
    }

    private sealed class WhenAllCompensationLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.WhenAll(
                    state => new KickoffChildWorkCommand(state.FlowId!, "child-a"),
                    state => new KickoffChildWorkCommand(state.FlowId!, "child-b"))
                .IfAnyFail(state => new RollbackFlowCommand(state.FlowId!));

            flow.Send(state => new FinalizeFlowCommand("after-when-all"));
        }
    }

    private sealed class IfDelayBranchLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.If(state => state.TakeThenBranch)
                .Delay(TimeSpan.FromMinutes(5))
                .Send(state => new FinalizeFlowCommand("if-branch"))
            .Else()
                .Send(state => new FinalizeFlowCommand("if-else"))
            .EndIf();

            flow.Send(state => new FinalizeFlowCommand("after-if"));
        }
    }

    private sealed class IfStoredBranchLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.If(state => state.TakeThenBranch)
                .Send(state => new FinalizeFlowCommand("if-then"))
            .Else()
                .Send(state => new FinalizeFlowCommand("if-else"))
            .EndIf();

            flow.Send(state => new FinalizeFlowCommand("after-if-branch"));
        }
    }

    private sealed class SwitchStoredBranchLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.Switch(state => state.RouteKey)
                .Case("alpha", branch => branch.Send(state => new FinalizeFlowCommand("switch-alpha")))
                .Case("beta", branch => branch.Send(state => new FinalizeFlowCommand("switch-beta")))
                .Default(branch => branch.Send(state => new FinalizeFlowCommand("switch-default")))
            .EndSwitch();

            flow.Send(state => new FinalizeFlowCommand("after-switch"));
        }
    }

    private sealed class NestedIfDelayBranchLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.If(state => state.TakeThenBranch)
                .If(state => state.TakeNestedThenBranch)
                    .Delay(TimeSpan.FromMinutes(5))
                    .Send(state => new FinalizeFlowCommand("nested-if-branch"))
                .Else()
                    .Send(state => new FinalizeFlowCommand("nested-if-else"))
                .EndIf()
                .Send(state => new FinalizeFlowCommand("after-nested-if"))
            .Else()
                .Send(state => new FinalizeFlowCommand("outer-else"))
            .EndIf();

            flow.Send(state => new FinalizeFlowCommand("after-nested-flow"));
        }
    }

    private sealed class ForEachResumeLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.ForEach(state => state.Items)
                .Configure((item, branch) => branch.Send(state => new ProcessItemLockdownCommand(item)))
                .EndForEach();

            flow.Send(state => new FinalizeFlowCommand("after-foreach"));
        }
    }

    private sealed class ForEachDelayUnsupportedLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.ForEach(state => state.Items)
                .Configure((item, branch) => branch.Delay(TimeSpan.FromMinutes(5)))
                .EndForEach();

            flow.Send(state => new FinalizeFlowCommand("after-foreach-delay"));
        }
    }

    private sealed class ParallelDelayResumeLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.Parallel()
                .Branch(branch =>
                {
                    branch.Delay(TimeSpan.FromMinutes(5));
                    branch.Send(state => new FinalizeFlowCommand("parallel-delayed"));
                })
                .Branch(branch => branch.Send(state => new FinalizeFlowCommand("parallel-immediate")))
            .EndParallel();

            flow.Send(state => new FinalizeFlowCommand("after-parallel"));
        }
    }

    private sealed class ParallelDelayFailedBranchLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.Parallel()
                .Branch(branch =>
                {
                    branch.Delay(TimeSpan.FromMinutes(5));
                    branch.Send(state => new FinalizeFlowCommand("parallel-delayed-failed"));
                })
                .Branch(branch => branch.Send(state => new FailFlowCommand("parallel-branch-fail")))
            .EndParallel();

            flow.Send(state => new FinalizeFlowCommand("after-parallel-failed"));
        }
    }

    private sealed class ParallelTwoDelayResumeLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.Parallel()
                .Branch(branch =>
                {
                    branch.Delay(TimeSpan.FromMinutes(5));
                    branch.Send(state => new FinalizeFlowCommand("parallel-left"));
                })
                .Branch(branch =>
                {
                    branch.Delay(TimeSpan.FromMinutes(10));
                    branch.Send(state => new FinalizeFlowCommand("parallel-right"));
                })
            .EndParallel();

            flow.Send(state => new FinalizeFlowCommand("after-parallel-both"));
        }
    }

    private sealed class ThrottleDelayResumeLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.Send(state => new FinalizeFlowCommand("before-throttle"));

            flow.Throttle(1)
                .Execute(inner =>
                {
                    inner.Delay(TimeSpan.FromMinutes(5));
                    inner.Send(state => new FinalizeFlowCommand("inside-throttle"));
                })
            .EndThrottle();

            flow.Send(state => new FinalizeFlowCommand("after-throttle"));
        }
    }

    private sealed class ThrottleResumeLockdownFlow : FlowConfig<LockdownE2EState>
    {
        protected override void Configure(IFlowBuilder<LockdownE2EState> flow)
        {
            flow.Throttle(1)
                .Execute(inner =>
                {
                    inner.Send(state => new FinalizeFlowCommand("throttle-1"));
                    inner.Send(state => new FinalizeFlowCommand("throttle-2"));
                    inner.Send(state => new FinalizeFlowCommand("throttle-3"));
                })
            .EndThrottle();

            flow.Send(state => new FinalizeFlowCommand("after-throttle-seq"));
        }
    }
}
