using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;

namespace Catga.Flow.Dsl;

/// <summary>Telemetry for DSL Flow execution.</summary>
public static class DslFlowTelemetry
{
    public static ActivitySource ActivitySource => CatgaActivitySource.Source;
    public static Counter<long> FlowsStarted => CatgaDiagnostics.FlowsStarted;
    public static Counter<long> FlowsCompleted => CatgaDiagnostics.FlowsCompleted;
    public static Counter<long> FlowsFailed => CatgaDiagnostics.FlowsFailed;
    public static Counter<long> StepsExecuted => CatgaDiagnostics.StepsExecuted;
    public static Histogram<double> FlowDuration => CatgaDiagnostics.FlowDuration;
    public static Histogram<double> StepDuration => CatgaDiagnostics.StepDuration;
}

/// <summary>
/// Executes flows defined by FlowConfig DSL.
/// </summary>
public partial class DslFlowExecutor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, TConfig> : IFlow<TState>
    where TState : class, IFlowState, new()
    where TConfig : FlowConfig<TState>
{
    private readonly ICatgaMediator _mediator;
    private readonly IDslFlowStore _store;
    private readonly TConfig _config;
    private readonly IFlowScheduler? _scheduler;
    private readonly List<ExecutedStep> _executedSteps = [];

    public DslFlowExecutor(ICatgaMediator mediator, IDslFlowStore store, TConfig config)
        : this(mediator, store, config, null)
    {
    }

    public DslFlowExecutor(ICatgaMediator mediator, IDslFlowStore store, TConfig config, IFlowScheduler? scheduler)
    {
        _mediator = mediator;
        _store = store;
        _config = config;
        _scheduler = scheduler;
        _config.Build();
    }

    // ========== Public API - Flow Execution ==========

    public async Task<DslFlowResult<TState>> RunAsync(TState state, CancellationToken cancellationToken = default)
    {
        state.FlowId ??= Guid.NewGuid().ToString("N");
        var flowName = _config.Name;
        var startTimestamp = Stopwatch.GetTimestamp();

        CatgaDiagnostics.IncrementActiveFlows();

        using var activity = DslFlowTelemetry.ActivitySource.StartActivity($"Flow.{flowName}");
        activity?.SetTag(CatgaActivitySource.Tags.FlowId, state.FlowId);
        activity?.SetTag(CatgaActivitySource.Tags.FlowName, flowName);
        activity?.SetTag("flow.type", typeof(TConfig).FullName);
        activity?.AddEvent(new ActivityEvent(CatgaActivitySource.Events.FlowStarted));

        DslFlowTelemetry.FlowsStarted.Add(1, new KeyValuePair<string, object?>("flow.name", flowName));

        var snapshot = new FlowSnapshot<TState>
        {
            FlowId = state.FlowId!,
            State = state,
            Position = FlowPosition.Initial,
            Status = DslFlowStatus.Running,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 0
        };

        await _store.CreateAsync(snapshot, cancellationToken);

        var result = await ExecuteFromStepAsync(snapshot, 0, cancellationToken);

        var elapsedMilliseconds = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        DslFlowTelemetry.FlowDuration.Record(elapsedMilliseconds, new KeyValuePair<string, object?>("flow.name", flowName));

        CatgaDiagnostics.DecrementActiveFlows();

        if (result.IsSuccess)
        {
            DslFlowTelemetry.FlowsCompleted.Add(1, new KeyValuePair<string, object?>("flow.name", flowName));
            activity?.AddEvent(new ActivityEvent(CatgaActivitySource.Events.FlowCompleted));
            activity?.SetTag(CatgaActivitySource.Tags.FlowStatus, "completed");
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            DslFlowTelemetry.FlowsFailed.Add(1, new KeyValuePair<string, object?>("flow.name", flowName));
            activity?.AddEvent(new ActivityEvent(CatgaActivitySource.Events.FlowFailed));
            activity?.SetTag(CatgaActivitySource.Tags.FlowStatus, "failed");
            activity?.SetTag(CatgaActivitySource.Tags.Error, result.Error);
            activity?.SetStatus(ActivityStatusCode.Error, result.Error);
        }

        activity?.SetTag(CatgaActivitySource.Tags.Duration, elapsedMilliseconds);

        return result;
    }

    public async Task<DslFlowResult<TState>> ResumeAsync(string flowId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.GetAsync<TState>(flowId, cancellationToken);

        if (snapshot == null)
            return DslFlowResult<TState>.Failure(DslFlowStatus.Failed, "Flow not found: " + flowId);

        if (snapshot.Status == DslFlowStatus.Completed)
            return DslFlowResult<TState>.Success(snapshot.State, DslFlowStatus.Completed);

        if (snapshot.Status == DslFlowStatus.Failed)
            return DslFlowResult<TState>.Failure(snapshot.State, DslFlowStatus.Failed, snapshot.Error);

        if (snapshot.Status == DslFlowStatus.Cancelled)
            return DslFlowResult<TState>.Failure(DslFlowStatus.Cancelled, "Flow was cancelled");

        // Handle suspended flow - check wait condition
        if (snapshot.Status == DslFlowStatus.Suspended)
        {
            var correlationId = $"{flowId}-step-{snapshot.Position.CurrentIndex}";
            var waitCondition = await _store.GetWaitConditionAsync(correlationId, cancellationToken);

            if (waitCondition != null)
            {
                var resumeResult = await CheckAndResumeFromWaitConditionAsync(snapshot, waitCondition, cancellationToken);
                if (resumeResult != null)
                    return resumeResult.Value;

                // Wait condition satisfied, continue from next step
                return await ExecuteFromStepAsync(snapshot, snapshot.Position.CurrentIndex + 1, cancellationToken);
            }
        }

        // Handle branch position recovery
        if (snapshot.Position.Depth > 0)
        {
            return await ResumeFromBranchPositionAsync(snapshot, cancellationToken);
        }

        return await ExecuteFromStepAsync(snapshot, snapshot.Position.CurrentIndex, cancellationToken);
    }

    private async Task<DslFlowResult<TState>?> CheckAndResumeFromWaitConditionAsync(
        FlowSnapshot<TState> snapshot,
        WaitCondition waitCondition,
        CancellationToken cancellationToken)
    {
        var state = snapshot.State;
        var step = _config.Steps[snapshot.Position.CurrentIndex];

        // Check timeout
        if (DateTime.UtcNow - waitCondition.CreatedAt > waitCondition.Timeout)
        {
            await UpdateSnapshotAsync(snapshot, state, snapshot.Position, DslFlowStatus.Failed, "WhenAll/WhenAny timeout", cancellationToken);
            return DslFlowResult<TState>.Failure(state, DslFlowStatus.Failed, "WhenAll/WhenAny timeout");
        }

        // Check if wait condition is satisfied
        if (waitCondition.Type == WaitType.All)
        {
            if (waitCondition.CompletedCount < waitCondition.ExpectedCount)
                return null; // Not ready yet

            // Check if any child failed
            var failedChild = waitCondition.Results.FirstOrDefault(r => !r.Success);
            if (failedChild != null)
            {
                // Execute compensation if configured
                if (step.HasCompensation && step.CreateCompensation != null)
                {
                    var request = step.CreateCompensation(state);
                    await _mediator.SendAsync(request, cancellationToken);
                }

                await UpdateSnapshotAsync(snapshot, state, snapshot.Position, DslFlowStatus.Failed, failedChild.Error, cancellationToken);
                return DslFlowResult<TState>.Failure(state, DslFlowStatus.Failed, failedChild.Error);
            }
        }
        else // WaitType.Any
        {
            var successChild = waitCondition.Results.FirstOrDefault(r => r.Success);
            if (successChild != null)
            {
                // Store result if configured
                if (step.SetResult != null && successChild.Result != null)
                {
                    step.SetResult(state, successChild.Result);
                }
            }
            else if (waitCondition.CompletedCount >= waitCondition.ExpectedCount)
            {
                // All failed
                var lastError = waitCondition.Results.LastOrDefault()?.Error ?? "All child flows failed";
                await UpdateSnapshotAsync(snapshot, state, snapshot.Position, DslFlowStatus.Failed, lastError, cancellationToken);
                return DslFlowResult<TState>.Failure(state, DslFlowStatus.Failed, lastError);
            }
            else
            {
                return null; // Not ready yet
            }
        }

        // Clear wait condition and continue
        await _store.ClearWaitConditionAsync(waitCondition.CorrelationId, cancellationToken);

        // Continue from next step
        return null;
    }

    public async Task<FlowSnapshot<TState>?> GetAsync(string flowId, CancellationToken cancellationToken = default)
    {
        return await _store.GetAsync<TState>(flowId, cancellationToken);
    }

    public async Task<bool> CancelAsync(string flowId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.GetAsync<TState>(flowId, cancellationToken);
        if (snapshot == null || snapshot.Status != DslFlowStatus.Running)
            return false;

        var cancelled = snapshot with
        {
            Status = DslFlowStatus.Cancelled,
            UpdatedAt = DateTime.UtcNow
        };

        return await _store.UpdateAsync(cancelled, cancellationToken);
    }

    // ========== Core Execution - Step Processing ==========

    private async Task<DslFlowResult<TState>> ExecuteFromStepAsync(
        FlowSnapshot<TState> snapshot,
        int startStep,
        CancellationToken cancellationToken)
    {
        var state = snapshot.State;
        var steps = _config.Steps;
        _executedSteps.Clear();

        for (var i = startStep; i < steps.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await UpdateSnapshotAsync(snapshot, state, StepToPosition(i), DslFlowStatus.Cancelled, null, cancellationToken);
                return DslFlowResult<TState>.Failure(DslFlowStatus.Cancelled, "Flow was cancelled");
            }

            var step = steps[i];
            var stepStartTimestamp = Stopwatch.GetTimestamp();
            var result = await ExecuteStepAsync(state, step, i, cancellationToken);

            var flowName = _config.Name;
            DslFlowTelemetry.StepsExecuted.Add(1,
                new KeyValuePair<string, object?>("flow.name", flowName),
                new KeyValuePair<string, object?>("step.index", i),
                new KeyValuePair<string, object?>("step.type", step.Type.ToString()));
            var stepElapsedMilliseconds = Stopwatch.GetElapsedTime(stepStartTimestamp).TotalMilliseconds;
            DslFlowTelemetry.StepDuration.Record(stepElapsedMilliseconds,
                new KeyValuePair<string, object?>("flow.name", flowName),
                new KeyValuePair<string, object?>("step.index", i));

            if (result.IsSuspended)
            {
                // Flow is suspended waiting for child flows
                await UpdateSnapshotAsync(snapshot, state, StepToPosition(i), DslFlowStatus.Suspended, null, cancellationToken);
                return DslFlowResult<TState>.Success(state, DslFlowStatus.Suspended);
            }

            if (!result.Success)
            {
                // Execute compensation for the failed step if it has one
                if (step.HasCompensation && step.CreateCompensation != null)
                {
                    var request = step.CreateCompensation(state);
                    await _mediator.SendAsync(request, cancellationToken);
                }

                // Execute compensations for previously successful steps in reverse order
                await ExecuteCompensationsAsync(state, cancellationToken);

                // Publish OnFlowFailed event
                if (_config.OnFlowFailedFactory != null)
                {
                    var failedEvent = _config.OnFlowFailedFactory(state, result.Error);
                    await _mediator.PublishAsync(failedEvent, cancellationToken);
                }

                await UpdateSnapshotAsync(snapshot, state, StepToPosition(i), DslFlowStatus.Failed, result.Error, cancellationToken);
                return DslFlowResult<TState>.Failure(state, DslFlowStatus.Failed, result.Error);
            }

            // Publish OnStepCompleted event
            if (_config.OnStepCompletedFactory != null)
            {
                var stepEvent = _config.OnStepCompletedFactory(state, i);
                await _mediator.PublishAsync(stepEvent, cancellationToken);
            }

            // Persist after step if tagged
            if (ShouldPersistAfterStep(step))
            {
                await UpdateSnapshotAsync(snapshot, state, StepToPosition(i + 1), DslFlowStatus.Running, null, cancellationToken);
            }
        }

        // Publish OnFlowCompleted event
        if (_config.OnFlowCompletedFactory != null)
        {
            var completedEvent = _config.OnFlowCompletedFactory(state);
            await _mediator.PublishAsync(completedEvent, cancellationToken);
        }

        await UpdateSnapshotAsync(snapshot, state, StepToPosition(steps.Count), DslFlowStatus.Completed, null, cancellationToken);
        return DslFlowResult<TState>.Success(state, DslFlowStatus.Completed);
    }

    private async Task<StepResult> ExecuteStepAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        // Check OnlyWhen condition
        if (step.HasCondition && !EvaluateCondition(state, step, stepIndex))
        {
            return StepResult.Skip();
        }

        try
        {
            return step.Type switch
            {
                StepType.Send => await ExecuteSendAsync(state, step, stepIndex, cancellationToken),
                StepType.Query => await ExecuteQueryAsync(state, step, stepIndex, cancellationToken),
                StepType.Publish => await ExecutePublishAsync(state, step, stepIndex, cancellationToken),
                StepType.WhenAll => await ExecuteWhenAllAsync(state, step, stepIndex, cancellationToken),
                StepType.WhenAny => await ExecuteWhenAnyAsync(state, step, stepIndex, cancellationToken),
                StepType.If => await ExecuteIfAsync(state, step, stepIndex, cancellationToken),
                StepType.Switch => await ExecuteSwitchAsync(state, step, stepIndex, cancellationToken),
                StepType.ForEach => await ExecuteForEachAsync(state, step, stepIndex, cancellationToken),
                StepType.Delay => await ExecuteDelayAsync(state, step, stepIndex, cancellationToken),
                StepType.ScheduleAt => await ExecuteScheduleAtAsync(state, step, stepIndex, cancellationToken),
                _ => StepResult.Failed($"Unknown step type: {step.Type}")
            };
        }
        catch (Exception) when (step.IsOptional)
        {
            // Optional steps don't fail the flow
            return StepResult.Skip();
        }
        catch (Exception ex)
        {
            return StepResult.Failed(ex.Message);
        }
    }

    private async Task<StepResult> ExecuteSendAsync(
    TState state,
    FlowStep step,
    int stepIndex,
    CancellationToken cancellationToken)
    {
        if (step.CreateRequest == null)
            return StepResult.Failed("No request factory configured");

        // Create the request using typed wrapper
        var request = step.CreateRequest(state);
        if (request == null)
            return StepResult.Failed("Request factory returned null");

        // Execute via mediator using pre-compiled delegate (no reflection, AOT-compatible)
        object? resultValue = null;

        if (step.ExecuteRequest != null)
        {
            var (isSuccess, error, value) = await step.ExecuteRequest(_mediator, request, cancellationToken);
            resultValue = value;

            if (!isSuccess)
            {
                if (step.IsOptional)
                    return StepResult.Skip();
                return StepResult.Failed(error ?? "Request failed");
            }

            // Check FailIf condition on result
            if (step.HasFailCondition && step.EvaluateFailCondition != null && resultValue != null)
            {
                var shouldFail = step.EvaluateFailCondition(state, resultValue);
                if (shouldFail)
                {
                    return StepResult.Failed(step.FailConditionMessage ?? "FailIf condition met");
                }
            }

            // Set result on state
            if (step.SetResult != null && resultValue != null)
            {
                step.SetResult(state, resultValue);
            }
        }
        else
        {
            // Fallback for IRequest without ExecuteRequest delegate
            var result = await _mediator.SendAsync((IRequest)request, cancellationToken);
            if (!result.IsSuccess)
            {
                if (step.IsOptional)
                    return StepResult.Skip();
                return StepResult.Failed(result.Error ?? "Request failed");
            }
        }

        // Track for compensation
        if (step.HasCompensation)
        {
            _executedSteps.Add(new ExecutedStep(stepIndex, step));
        }

        return StepResult.Succeeded(resultValue);
    }

    private async Task<StepResult> ExecuteQueryAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        // Query is same as Send with result
        return await ExecuteSendAsync(state, step, stepIndex, cancellationToken);
    }

    private async Task<StepResult> ExecutePublishAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.CreateRequest == null)
            return StepResult.Failed("No event factory configured");

        var @event = step.CreateRequest(state) as IEvent;
        if (@event == null)
            return StepResult.Failed("Event factory returned null");

        await _mediator.PublishAsync(@event, cancellationToken);
        return StepResult.Succeeded();
    }

    private async Task<StepResult> ExecuteWhenAllAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        // Start all child requests
        if (step.ChildRequestFactories == null || step.ChildRequestFactories.Count == 0)
            return StepResult.Failed("No child requests configured for WhenAll");

        var childFlowIds = new List<string>();
        if (step.CreateChildRequests != null)
        {
            foreach (var factory in step.CreateChildRequests)
            {
                var req = factory(state);
                await _mediator.SendAsync(req, cancellationToken);
                childFlowIds.Add(Guid.NewGuid().ToString("N")); // In real impl, get from request
            }
        }

        // Create wait condition
        var correlationId = $"{state.FlowId}-step-{stepIndex}";
        var waitCondition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.All,
            ExpectedCount = step.ChildRequestCount,
            CompletedCount = 0,
            Timeout = step.Timeout ?? TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow,
            FlowId = state.FlowId!,
            FlowType = _config.GetType().Name,
            Step = stepIndex,
            ChildFlowIds = childFlowIds
        };

        await _store.SetWaitConditionAsync(correlationId, waitCondition, cancellationToken);

        // Return suspended status
        return StepResult.Suspended();
    }

    private async Task<StepResult> ExecuteWhenAnyAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        // Start all child requests
        if (step.ChildRequestFactories == null || step.ChildRequestFactories.Count == 0)
            return StepResult.Failed("No child requests configured for WhenAny");

        var childFlowIds = new List<string>();
        if (step.CreateChildRequests != null)
        {
            foreach (var factory in step.CreateChildRequests)
            {
                var req = factory(state);
                await _mediator.SendAsync(req, cancellationToken);
                childFlowIds.Add(Guid.NewGuid().ToString("N"));
            }
        }

        // Create wait condition
        var correlationId = $"{state.FlowId}-step-{stepIndex}";
        var waitCondition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.Any,
            ExpectedCount = step.ChildRequestCount,
            CompletedCount = 0,
            Timeout = step.Timeout ?? TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow,
            FlowId = state.FlowId!,
            FlowType = _config.GetType().Name,
            Step = stepIndex,
            CancelOthers = true,
            ChildFlowIds = childFlowIds
        };

        await _store.SetWaitConditionAsync(correlationId, waitCondition, cancellationToken);

        return StepResult.Suspended();
    }

    private async Task<StepResult> ExecuteDelayAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (_scheduler == null)
            return StepResult.Failed("No IFlowScheduler configured. Add UseQuartzScheduling() to enable delayed execution.");

        if (step.DelayDuration == null || step.DelayDuration.Value <= TimeSpan.Zero)
            return StepResult.Succeeded(); // No delay, continue immediately

        var resumeAt = DateTimeOffset.UtcNow.Add(step.DelayDuration.Value);
        var scheduleId = await _scheduler.ScheduleResumeAsync(
            state.FlowId!,
            state.FlowId!,
            resumeAt,
            cancellationToken);

        // Store schedule info for potential cancellation
        var correlationId = $"{state.FlowId}-step-{stepIndex}";
        var waitCondition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = step.DelayDuration.Value.Add(TimeSpan.FromMinutes(1)),
            CreatedAt = DateTime.UtcNow,
            FlowId = state.FlowId!,
            FlowType = _config.GetType().Name,
            Step = stepIndex,
            ScheduleId = scheduleId
        };

        await _store.SetWaitConditionAsync(correlationId, waitCondition, cancellationToken);

        return StepResult.Suspended();
    }

    private async Task<StepResult> ExecuteScheduleAtAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (_scheduler == null)
            return StepResult.Failed("No IFlowScheduler configured. Add UseQuartzScheduling() to enable scheduled execution.");

        if (step.GetScheduleTime == null)
            return StepResult.Failed("No schedule time selector configured for ScheduleAt step");

        var scheduleTime = step.GetScheduleTime(state);
        var resumeAt = new DateTimeOffset(scheduleTime, TimeSpan.Zero);

        // If schedule time is in the past, continue immediately
        if (resumeAt <= DateTimeOffset.UtcNow)
            return StepResult.Succeeded();

        var scheduleId = await _scheduler.ScheduleResumeAsync(
            state.FlowId!,
            state.FlowId!,
            resumeAt,
            cancellationToken);

        // Store schedule info
        var correlationId = $"{state.FlowId}-step-{stepIndex}";
        var timeout = resumeAt - DateTimeOffset.UtcNow;
        var waitCondition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = timeout.Add(TimeSpan.FromMinutes(1)),
            CreatedAt = DateTime.UtcNow,
            FlowId = state.FlowId!,
            FlowType = _config.GetType().Name,
            Step = stepIndex,
            ScheduleId = scheduleId
        };

        await _store.SetWaitConditionAsync(correlationId, waitCondition, cancellationToken);

        return StepResult.Suspended();
    }

    private async Task ExecuteCompensationsAsync(TState state, CancellationToken cancellationToken)
    {
        var flowName = _config.Name;

        // Execute compensations in reverse order
        for (var i = _executedSteps.Count - 1; i >= 0; i--)
        {
            var executed = _executedSteps[i];
            if (executed.Step.CompensationFactory == null)
                continue;

            if (executed.Step.CreateCompensation != null)
            {
                var request = executed.Step.CreateCompensation(state);
                await _mediator.SendAsync(request, cancellationToken);
            }
        }
    }

    private bool EvaluateCondition(TState state, FlowStep step, int stepIndex)
    {
        if (step.EvaluateCondition == null)
            return true;

        return step.EvaluateCondition(state);
    }

    private bool ShouldPersistAfterStep(FlowStep step)
    {
        foreach (var tag in step.Tags)
        {
            if (_config.ShouldPersistForTag(tag))
                return true;
        }
        return false;
    }

    private async Task UpdateSnapshotAsync(
        FlowSnapshot<TState> original,
        TState state,
        FlowPosition position,
        DslFlowStatus status,
        string? error,
        CancellationToken cancellationToken)
    {
        var updated = original with
        {
            State = state,
            Position = position,
            Status = status,
            Error = error,
            UpdatedAt = DateTime.UtcNow,
            Version = original.Version + 1
        };

        await _store.UpdateAsync(updated, cancellationToken);
    }

    // ========== Position Navigation - Branch Support ==========

    private FlowPosition StepToPosition(int stepIndex) => new([stepIndex]);

    private FlowStep? GetStepAtPosition(FlowPosition position)
    {
        if (position.Path.Length == 0)
            return null;

        var steps = _config.Steps;
        FlowStep? current = null;

        for (int depth = 0; depth < position.Path.Length; depth++)
        {
            var index = position.Path[depth];

            if (depth == 0)
            {
                // Top level
                if (index < 0 || index >= steps.Count)
                    return null;
                current = steps[index];
            }
            else if (current != null)
            {
                // Inside a branch - index is branch selector or step index
                List<FlowStep>? branchSteps = null;

                if (current.Type == StepType.If)
                {
                    var branchIndex = position.Path[depth];
                    if (branchIndex == 0)
                        branchSteps = current.ThenBranch;
                    else if (branchIndex == -1)
                        branchSteps = current.ElseBranch;
                    else if (current.ElseIfBranches != null && branchIndex > 0 && branchIndex <= current.ElseIfBranches.Count)
                        branchSteps = current.ElseIfBranches[branchIndex - 1].Steps;
                }
                else if (current.Type == StepType.Switch)
                {
                    var caseIndex = position.Path[depth];
                    if (caseIndex == -1)
                        branchSteps = current.DefaultBranch;
                    else if (current.Cases != null)
                    {
                        var caseList = current.Cases.Values.ToList();
                        if (caseIndex >= 0 && caseIndex < caseList.Count)
                            branchSteps = caseList[caseIndex];
                    }
                }

                if (branchSteps == null)
                    return null;

                // Next element is step index within branch
                depth++;
                if (depth >= position.Path.Length)
                    return null;

                var stepIndex = position.Path[depth];
                if (stepIndex < 0 || stepIndex >= branchSteps.Count)
                    return null;

                current = branchSteps[stepIndex];
            }
        }

        return current;
    }

    /// <summary>
    /// Get the branch steps at a given position.
    /// </summary>
    private List<FlowStep>? GetBranchStepsAtPosition(FlowPosition position)
    {
        if (position.Path.Length < 2)
            return null;

        var parentPosition = new FlowPosition(position.Path[..^1].ToArray());
        var step = GetStepAtPosition(new FlowPosition(position.Path[..^2].ToArray()));

        if (step == null)
            return null;

        var branchIndex = position.Path[^2];

        if (step.Type == StepType.If)
        {
            if (branchIndex == 0)
                return step.ThenBranch;
            if (branchIndex == -1)
                return step.ElseBranch;
            if (step.ElseIfBranches != null && branchIndex > 0 && branchIndex <= step.ElseIfBranches.Count)
                return step.ElseIfBranches[branchIndex - 1].Steps;
        }
        else if (step.Type == StepType.Switch)
        {
            if (branchIndex == -1)
                return step.DefaultBranch;
            if (step.Cases != null)
            {
                var caseList = step.Cases.Values.ToList();
                if (branchIndex >= 0 && branchIndex < caseList.Count)
                    return caseList[branchIndex];
            }
        }

        return null;
    }

    private async Task<DslFlowResult<TState>> ResumeFromBranchPositionAsync(
        FlowSnapshot<TState> snapshot,
        CancellationToken cancellationToken)
    {
        var state = snapshot.State;
        var position = snapshot.Position;

        // Navigate to the correct branch and resume execution
        var stepIndex = position.Path[0]; // First element is the main step index
        if (stepIndex >= _config.Steps.Count)
        {
            return DslFlowResult<TState>.Failure(DslFlowStatus.Failed,
                $"Invalid step index {stepIndex} in position {string.Join(",", position.Path)}");
        }

        var step = _config.Steps[stepIndex];

        // Resume execution from the branch position
        var result = await ResumeBranchStepAsync(state, step, position, stepIndex, cancellationToken);

        if (!result.Success)
        {
            await UpdateSnapshotAsync(snapshot, state, position, DslFlowStatus.Failed, result.Error, cancellationToken);
            return DslFlowResult<TState>.Failure(state, DslFlowStatus.Failed, result.Error);
        }

        // Continue with remaining steps after the branch step
        return await ExecuteFromStepAsync(snapshot, stepIndex + 1, cancellationToken);
    }

    private async Task<StepResult> ResumeBranchStepAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        return step.Type switch
        {
            StepType.ForEach => await ResumeForEachAsync(state, step, position, stepIndex, cancellationToken),
            StepType.If or StepType.Switch => await ExecuteStepAsync(state, step, stepIndex, cancellationToken),
            _ => await ExecuteStepAsync(state, step, stepIndex, cancellationToken)
        };
    }

    private record struct ExecutedStep(int Index, FlowStep Step);

    private readonly struct StepResult
    {
        public bool Success { get; }
        public bool Skipped { get; }
        public string? Error { get; }
        public object? Result { get; }
        public bool IsSuspended { get; }

        private StepResult(bool success, bool skipped, bool suspended, string? error, object? result)
        {
            Success = success;
            Skipped = skipped;
            IsSuspended = suspended;
            Error = error;
            Result = result;
        }

        public static StepResult Succeeded(object? result = null) => new(true, false, false, null, result);
        public static StepResult Failed(string error) => new(false, false, false, error, null);
        public static StepResult Skip() => new(true, true, false, null, null);
        public static StepResult Suspended() => new(true, false, true, null, null);
    }
}

/// <summary>
/// Result of a flow step execution.
/// </summary>
public readonly struct StepResult
{
    public bool Success { get; }
    public bool Skipped { get; }
    public string? Error { get; }
    public object? Result { get; }
    public bool IsSuspended { get; }

    private StepResult(bool success, bool skipped, bool suspended, string? error, object? result)
    {
        Success = success;
        Skipped = skipped;
        IsSuspended = suspended;
        Error = error;
        Result = result;
    }

    public static StepResult Succeeded(object? result = null) => new(true, false, false, null, result);
    public static StepResult Failed(string error) => new(false, false, false, error, null);
    public static StepResult Skip() => new(true, true, false, null, null);
    public static StepResult Suspended() => new(true, false, true, null, null);
}
