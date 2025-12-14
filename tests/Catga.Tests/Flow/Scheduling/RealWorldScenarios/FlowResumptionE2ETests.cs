using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling.RealWorldScenarios;

/// <summary>
/// E2E tests for flow suspension and resumption scenarios.
/// </summary>
public class FlowResumptionE2ETests
{
    #region Test Infrastructure

    public class WorkflowState : IFlowState
    {
        public string? FlowId { get; set; }
        public string WorkflowId { get; set; } = Guid.NewGuid().ToString("N");
        public int CurrentStep { get; set; }
        public List<string> ExecutedSteps { get; set; } = [];
        public bool IsPaused { get; set; }
        public DateTime? ResumeAt { get; set; }
        public Dictionary<string, object> Context { get; set; } = [];
    }

    // Commands
    public record ExecuteStepCommand(string WorkflowId, int Step) : IRequest<StepResult>;
    public record SaveCheckpointCommand(string WorkflowId, int Step) : IRequest;
    public record LoadCheckpointCommand(string WorkflowId) : IRequest<WorkflowCheckpoint>;

    public record StepResult(bool Success, string Message);
    public record WorkflowCheckpoint(int Step, Dictionary<string, object> Context);

    #endregion

    #region Scenario 1: Suspend and Resume Flow

    /// <summary>
    /// Scenario: Flow suspends at delay step and can be resumed later
    /// </summary>
    [Fact]
    public async Task FlowSuspension_ShouldSaveStateAndScheduleResume()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        string? capturedFlowId = null;
        string? capturedScheduleId = null;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedFlowId = callInfo.ArgAt<string>(0);
                capturedScheduleId = $"resume-{Guid.NewGuid():N}";
                return ValueTask.FromResult(capturedScheduleId);
            });

        mediator.SendAsync(Arg.Any<ExecuteStepCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<StepResult>.Success(new StepResult(true, "Step completed")));

        var config = new SuspendResumeFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<WorkflowState, SuspendResumeFlowConfig>(mediator, store, config, scheduler);
        var state = new WorkflowState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        capturedFlowId.Should().NotBeNullOrEmpty();
        capturedScheduleId.Should().NotBeNullOrEmpty();

        // Verify state was saved
        var savedSnapshot = await store.GetAsync<WorkflowState>(result.State.FlowId!);
        savedSnapshot.Should().NotBeNull();
    }

    private class SuspendResumeFlowConfig : FlowConfig<WorkflowState>
    {
        public override string FlowId => "suspend-resume";

        protected override void Configure(IFlowBuilder<WorkflowState> flow)
        {
            flow
                .Send<ExecuteStepCommand, StepResult>(s => new ExecuteStepCommand(s.WorkflowId, 1))
                .Delay(TimeSpan.FromMinutes(30)) // Suspend point
                .Send<ExecuteStepCommand, StepResult>(s => new ExecuteStepCommand(s.WorkflowId, 2));
        }
    }

    #endregion

    #region Scenario 2: Resume from Checkpoint

    /// <summary>
    /// Scenario: Flow resumes from last checkpoint after system restart
    /// </summary>
    [Fact]
    public async Task FlowCheckpoint_ShouldResumeFromLastState()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var scheduleIds = new List<string>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var id = $"checkpoint-{scheduleIds.Count + 1}";
                scheduleIds.Add(id);
                return ValueTask.FromResult(id);
            });

        mediator.SendAsync(Arg.Any<SaveCheckpointCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult.Success());

        var config = new CheckpointFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<WorkflowState, CheckpointFlowConfig>(mediator, store, config, scheduler);
        var state = new WorkflowState { CurrentStep = 0 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        scheduleIds.Should().HaveCount(1);
    }

    private class CheckpointFlowConfig : FlowConfig<WorkflowState>
    {
        public override string FlowId => "checkpoint";

        protected override void Configure(IFlowBuilder<WorkflowState> flow)
        {
            flow
                .Send(s => new SaveCheckpointCommand(s.WorkflowId, 1))
                .Delay(TimeSpan.FromHours(1)) // Save and wait
                .Send(s => new SaveCheckpointCommand(s.WorkflowId, 2));
        }
    }

    #endregion

    #region Scenario 3: Conditional Resume

    /// <summary>
    /// Scenario: Flow waits conditionally based on external state
    /// </summary>
    [Fact]
    public async Task ConditionalResume_ShouldScheduleBasedOnState()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var wasScheduled = false;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                wasScheduled = true;
                return ValueTask.FromResult("conditional-resume");
            });

        var config = new ConditionalResumeFlowConfig();
        config.Build();

        // State indicates we need to wait
        var executor = new DslFlowExecutor<WorkflowState, ConditionalResumeFlowConfig>(mediator, store, config, scheduler);
        var state = new WorkflowState { IsPaused = true };
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        wasScheduled.Should().BeTrue();
    }

    [Fact]
    public async Task ConditionalResume_ShouldSkipDelayWhenNotPaused()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        mediator.SendAsync(Arg.Any<ExecuteStepCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<StepResult>.Success(new StepResult(true, "done")));

        var config = new ConditionalResumeFlowConfig();
        config.Build();

        // State indicates no wait needed
        var executor = new DslFlowExecutor<WorkflowState, ConditionalResumeFlowConfig>(mediator, store, config, scheduler);
        var state = new WorkflowState { IsPaused = false };
        var result = await executor.RunAsync(state);

        // Assert - Should complete without scheduling
        await scheduler.DidNotReceive().ScheduleResumeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private class ConditionalResumeFlowConfig : FlowConfig<WorkflowState>
    {
        public override string FlowId => "conditional-resume";

        protected override void Configure(IFlowBuilder<WorkflowState> flow)
        {
            flow
                .If(s => s.IsPaused)
                    .Delay(TimeSpan.FromHours(2))
                .EndIf()
                .Send<ExecuteStepCommand, StepResult>(s => new ExecuteStepCommand(s.WorkflowId, 1));
        }
    }

    #endregion

    #region Scenario 4: Scheduled Resume at Specific Time

    /// <summary>
    /// Scenario: Flow resumes at a specific scheduled time
    /// </summary>
    [Fact]
    public async Task ScheduledResume_ShouldResumeAtExactTime()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        DateTimeOffset capturedTime = default;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedTime = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("scheduled-resume");
            });

        var config = new ScheduledResumeFlowConfig();
        config.Build();

        var resumeTime = DateTime.UtcNow.AddHours(5);
        var executor = new DslFlowExecutor<WorkflowState, ScheduledResumeFlowConfig>(mediator, store, config, scheduler);
        var state = new WorkflowState { ResumeAt = resumeTime };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        capturedTime.UtcDateTime.Should().BeCloseTo(resumeTime, TimeSpan.FromSeconds(1));
    }

    private class ScheduledResumeFlowConfig : FlowConfig<WorkflowState>
    {
        public override string FlowId => "scheduled-resume";

        protected override void Configure(IFlowBuilder<WorkflowState> flow)
        {
            flow
                .ScheduleAt(s => s.ResumeAt ?? DateTime.UtcNow)
                .Send<ExecuteStepCommand, StepResult>(s => new ExecuteStepCommand(s.WorkflowId, 1));
        }
    }

    #endregion

    #region Scenario 5: Cancel Scheduled Resume

    /// <summary>
    /// Scenario: Cancel a previously scheduled resume
    /// </summary>
    [Fact]
    public async Task CancelScheduledResume_ShouldRemoveSchedule()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var scheduleId = "test-schedule-123";

        scheduler.CancelScheduledResumeAsync(scheduleId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        // Act
        var result = await scheduler.CancelScheduledResumeAsync(scheduleId);

        // Assert
        result.Should().BeTrue();
        await scheduler.Received(1).CancelScheduledResumeAsync(scheduleId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelNonExistentSchedule_ShouldReturnFalse()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.CancelScheduledResumeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        // Act
        var result = await scheduler.CancelScheduledResumeAsync("non-existent");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helper

    private static InMemoryTestStore CreateInMemoryStore() => new();

    private class InMemoryTestStore : IDslFlowStore
    {
        private readonly Dictionary<string, object> _flows = new();
        private readonly Dictionary<string, WaitCondition> _waitConditions = new();
        private readonly Dictionary<string, ForEachProgress> _forEachProgress = new();

        public Task<bool> CreateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default) where TState : class, IFlowState
        { _flows[snapshot.FlowId] = snapshot; return Task.FromResult(true); }
        public Task<FlowSnapshot<TState>?> GetAsync<TState>(string flowId, CancellationToken ct = default) where TState : class, IFlowState
            => Task.FromResult(_flows.TryGetValue(flowId, out var s) ? (FlowSnapshot<TState>?)s : null);
        public Task<bool> UpdateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default) where TState : class, IFlowState
        { _flows[snapshot.FlowId] = snapshot; return Task.FromResult(true); }
        public Task<bool> DeleteAsync(string flowId, CancellationToken ct = default) => Task.FromResult(_flows.Remove(flowId));
        public Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        { _waitConditions[correlationId] = condition; return Task.CompletedTask; }
        public Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
            => Task.FromResult(_waitConditions.TryGetValue(correlationId, out var c) ? c : null);
        public Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        { _waitConditions[correlationId] = condition; return Task.CompletedTask; }
        public Task ClearWaitConditionAsync(string correlationId, CancellationToken ct = default)
        { _waitConditions.Remove(correlationId); return Task.CompletedTask; }
        public Task<IReadOnlyList<WaitCondition>> GetTimedOutWaitConditionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WaitCondition>>(_waitConditions.Values.Where(c => DateTime.UtcNow - c.CreatedAt > c.Timeout).ToList());
        public Task SaveForEachProgressAsync(string flowId, int stepIndex, ForEachProgress progress, CancellationToken ct = default)
        { _forEachProgress[$"{flowId}:{stepIndex}"] = progress; return Task.CompletedTask; }
        public Task<ForEachProgress?> GetForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
            => Task.FromResult(_forEachProgress.TryGetValue($"{flowId}:{stepIndex}", out var p) ? p : null);
        public Task ClearForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
        { _forEachProgress.Remove($"{flowId}:{stepIndex}"); return Task.CompletedTask; }
    }

    #endregion
}
