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
    private readonly IRequestClientFactory? _requestClientFactory;
    private readonly List<ExecutedStep> _executedSteps = [];

    public DslFlowExecutor(
        ICatgaMediator mediator,
        IDslFlowStore store,
        TConfig config,
        IFlowScheduler? scheduler = null,
        IRequestClientFactory? requestClientFactory = null)
    {
        _mediator = mediator;
        _store = store;
        _config = config;
        _scheduler = scheduler;
        _requestClientFactory = requestClientFactory;
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

        if (!await _store.CreateAsync(snapshot, cancellationToken))
        {
            CatgaDiagnostics.DecrementActiveFlows();
            return DslFlowResult<TState>.Failure(state, DslFlowStatus.Failed, $"Flow already exists: {state.FlowId}");
        }

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
            if (snapshot.Position.Depth == 0)
            {
                var recoverableResume = await TryResumeTopLevelRecoverableStepAsync(snapshot, cancellationToken);
                if (recoverableResume != null)
                    return recoverableResume.Value;
            }

            var correlationId = BuildWaitConditionCorrelationId(flowId, snapshot.Position);
            var waitCondition = await _store.GetWaitConditionAsync(correlationId, cancellationToken);

            if (waitCondition != null)
            {
                var step = GetStepAtPosition(snapshot.Position);
                if (step == null)
                    return DslFlowResult<TState>.Failure(snapshot.State, DslFlowStatus.Failed, "Unable to resolve flow step at stored position");

                var waitResult = await EvaluateWaitConditionAsync(snapshot.State, step, snapshot.Position, waitCondition, cancellationToken);
                if (waitResult.IsSuspended)
                    return DslFlowResult<TState>.Success(snapshot.State, DslFlowStatus.Suspended);

                if (!waitResult.Success)
                {
                    await PublishStepFailedAsync(snapshot.State, GetTopLevelStepIndex(snapshot.Position), waitResult.Error, cancellationToken);
                    await PublishFlowFailedAsync(snapshot.State, waitResult.Error, cancellationToken);
                    await UpdateSnapshotAsync(snapshot, snapshot.State, snapshot.Position, DslFlowStatus.Failed, waitResult.Error, cancellationToken);
                    return DslFlowResult<TState>.Failure(snapshot.State, DslFlowStatus.Failed, waitResult.Error);
                }

                if (snapshot.Position.Depth > 0)
                    return await ResumeFromBranchPositionAsync(snapshot, resumeCurrentStep: false, cancellationToken);

                // Wait condition satisfied, continue from next step
                return await ExecuteFromStepAsync(snapshot, snapshot.Position.CurrentIndex + 1, cancellationToken);
            }
        }

        // Handle branch position recovery
        if (snapshot.Position.Depth > 0)
        {
            return await ResumeFromBranchPositionAsync(snapshot, resumeCurrentStep: true, cancellationToken);
        }

        var recoverableTopLevelResume = await TryResumeTopLevelRecoverableStepAsync(snapshot, cancellationToken);
        if (recoverableTopLevelResume != null)
            return recoverableTopLevelResume.Value;

        return await ExecuteFromStepAsync(snapshot, snapshot.Position.CurrentIndex, cancellationToken);
    }

    private async Task<StepResult> EvaluateWaitConditionAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        WaitCondition waitCondition,
        CancellationToken cancellationToken)
    {
        // Check timeout
        if (DateTime.UtcNow - waitCondition.CreatedAt > waitCondition.Timeout)
        {
            return StepResult.Failed("WhenAll/WhenAny timeout", position);
        }

        // Check if wait condition is satisfied
        if (waitCondition.Type == WaitType.All)
        {
            if (waitCondition.CompletedCount < waitCondition.ExpectedCount)
                return StepResult.Suspended(position);

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

                return StepResult.Failed(failedChild.Error ?? "Child flow failed", position);
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
                return StepResult.Failed(lastError, position);
            }
            else
            {
                return StepResult.Suspended(position);
            }
        }

        // Clear wait condition and continue
        await _store.ClearWaitConditionAsync(waitCondition.CorrelationId, cancellationToken);

        return StepResult.Succeeded(position: position);
    }

    private async Task<DslFlowResult<TState>?> TryResumeTopLevelRecoverableStepAsync(
        FlowSnapshot<TState> snapshot,
        CancellationToken cancellationToken)
    {
        var currentStepIndex = snapshot.Position.CurrentIndex;
        if (currentStepIndex < 0 || currentStepIndex >= _config.Steps.Count)
            return null;

        var state = snapshot.State;
        var step = _config.Steps[currentStepIndex];
        if (string.IsNullOrEmpty(state.FlowId))
            return null;

        StepResult? result = null;
        if (step.Type == StepType.ForEach)
        {
            var progress = await _store.GetForEachProgressAsync(state.FlowId!, currentStepIndex, cancellationToken);
            if (progress != null)
                result = await ResumeForEachAsync(state, step, snapshot.Position, currentStepIndex, cancellationToken);
        }
        else if (step.Type == StepType.Parallel)
        {
            var parallelProgress = await _store.GetParallelProgressAsync(state.FlowId!, currentStepIndex, cancellationToken);
            if (parallelProgress != null)
                result = await ResumeParallelAsync(state, step, currentStepIndex, parallelProgress, cancellationToken);
        }

        if (!result.HasValue)
            return null;

        var stepResult = result.Value;
        if (!stepResult.Success)
        {
            await PublishStepFailedAsync(state, currentStepIndex, stepResult.Error, cancellationToken);
            await PublishFlowFailedAsync(state, stepResult.Error, cancellationToken);
            await UpdateSnapshotAsync(snapshot, state, stepResult.Position ?? snapshot.Position, DslFlowStatus.Failed, stepResult.Error, cancellationToken);
            return DslFlowResult<TState>.Failure(state, DslFlowStatus.Failed, stepResult.Error);
        }

        if (stepResult.IsSuspended)
        {
            await UpdateSnapshotAsync(snapshot, state, stepResult.Position ?? snapshot.Position, DslFlowStatus.Suspended, null, cancellationToken);
            return DslFlowResult<TState>.Success(state, DslFlowStatus.Suspended);
        }

        return await ExecuteFromStepAsync(snapshot, currentStepIndex + 1, cancellationToken);
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

        var nextVersionCancelled = snapshot with
        {
            Status = DslFlowStatus.Cancelled,
            UpdatedAt = DateTime.UtcNow,
            Version = snapshot.Version + 1
        };

        return await TryPersistSnapshotAsync(snapshot, nextVersionCancelled, cancellationToken);
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
                snapshot = await UpdateSnapshotAsync(snapshot, state, StepToPosition(i), DslFlowStatus.Cancelled, null, cancellationToken);
                return DslFlowResult<TState>.Failure(DslFlowStatus.Cancelled, "Flow was cancelled");
            }

            var step = steps[i];
            var stepStartTimestamp = Stopwatch.GetTimestamp();
            var position = StepToPosition(i);
            var result = await ExecuteStepAsync(state, step, position, cancellationToken);

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
                snapshot = await UpdateSnapshotAsync(snapshot, state, result.Position ?? position, DslFlowStatus.Suspended, null, cancellationToken);
                return DslFlowResult<TState>.Success(state, DslFlowStatus.Suspended);
            }

            if (!result.Success)
            {
                await PublishStepFailedAsync(state, i, result.Error, cancellationToken);

                // Execute compensation for the failed step if it has one
                if (step.HasCompensation && step.CreateCompensation != null)
                {
                    var request = step.CreateCompensation(state);
                    await _mediator.SendAsync(request, cancellationToken);
                }

                // Execute compensations for previously successful steps in reverse order
                await ExecuteCompensationsAsync(state, cancellationToken);

                // Publish OnFlowFailed event
                await PublishFlowFailedAsync(state, result.Error, cancellationToken);

                snapshot = await UpdateSnapshotAsync(snapshot, state, result.Position ?? position, DslFlowStatus.Failed, result.Error, cancellationToken);
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
                snapshot = await UpdateSnapshotAsync(snapshot, state, StepToPosition(i + 1), DslFlowStatus.Running, null, cancellationToken);
            }
        }

        // Publish OnFlowCompleted event
        if (_config.OnFlowCompletedFactory != null)
        {
            var completedEvent = _config.OnFlowCompletedFactory(state);
            await _mediator.PublishAsync(completedEvent, cancellationToken);
        }

        snapshot = await UpdateSnapshotAsync(snapshot, state, StepToPosition(steps.Count), DslFlowStatus.Completed, null, cancellationToken);
        return DslFlowResult<TState>.Success(state, DslFlowStatus.Completed);
    }

    private Task<StepResult> ExecuteStepAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
        => ExecuteStepAsync(state, step, StepToPosition(stepIndex), cancellationToken);

    private async Task<StepResult> ExecuteStepAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        CancellationToken cancellationToken)
    {
        var stepIndex = position.CurrentIndex;

        // Check OnlyWhen condition
        if (step.HasCondition && !EvaluateCondition(state, step, stepIndex))
        {
            return StepResult.Skip(position);
        }

        try
        {
            return step.Type switch
            {
                StepType.Send => await ExecuteSendAsync(state, step, stepIndex, cancellationToken),
                StepType.Query => await ExecuteQueryAsync(state, step, stepIndex, cancellationToken),
                StepType.Publish => await ExecutePublishAsync(state, step, stepIndex, cancellationToken),
                StepType.WhenAll => await ExecuteWhenAllAsync(state, step, position, cancellationToken),
                StepType.WhenAny => await ExecuteWhenAnyAsync(state, step, position, cancellationToken),
                StepType.If => await ExecuteIfAsync(state, step, position, cancellationToken),
                StepType.Switch => await ExecuteSwitchAsync(state, step, position, cancellationToken),
                StepType.ForEach => await ExecuteForEachAsync(state, step, stepIndex, cancellationToken),
                StepType.Delay => await ExecuteDelayAsync(state, step, position, cancellationToken),
                StepType.ScheduleAt => await ExecuteScheduleAtAsync(state, step, position, cancellationToken),
                StepType.Parallel => await ExecuteParallelAsync(state, step, stepIndex, cancellationToken),
                StepType.Throttle => await ExecuteThrottleAsync(state, step, position, cancellationToken),
                StepType.RemoteSend => await ExecuteRemoteSendAsync(state, step, stepIndex, cancellationToken),
                _ => StepResult.Failed($"Unknown step type: {step.Type}", position)
            };
        }
        catch (Exception) when (step.IsOptional)
        {
            // Optional steps don't fail the flow
            return StepResult.Skip(position);
        }
        catch (Exception ex)
        {
            return StepResult.Failed(ex.Message, position);
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

    private async Task<StepResult> ExecuteRemoteSendAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.CreateRequest == null)
            return StepResult.Failed("No request factory configured");

        if (_requestClientFactory == null)
            return StepResult.Failed("IRequestClientFactory not registered. Call UseRequestClient() to enable RemoteSend.");

        var request = step.CreateRequest(state);
        if (request == null)
            return StepResult.Failed("Request factory returned null");

        if (step.ExecuteRemoteRequest == null)
            return StepResult.Failed("No remote request executor configured");

        var (isSuccess, error, resultValue) = await step.ExecuteRemoteRequest(
            _requestClientFactory,
            request,
            cancellationToken);

        if (!isSuccess)
        {
            if (step.IsOptional)
                return StepResult.Skip();
            return StepResult.Failed(error ?? "Remote request failed");
        }

        if (step.HasFailCondition && step.EvaluateFailCondition != null && resultValue != null)
        {
            var shouldFail = step.EvaluateFailCondition(state, resultValue);
            if (shouldFail)
                return StepResult.Failed(step.FailConditionMessage ?? "FailIf condition met");
        }

        if (step.SetResult != null && resultValue != null)
            step.SetResult(state, resultValue);

        if (step.HasCompensation)
            _executedSteps.Add(new ExecutedStep(stepIndex, step));

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
        FlowPosition position,
        CancellationToken cancellationToken)
    {
        var topLevelStepIndex = GetTopLevelStepIndex(position);

        // Start all child requests
        if (step.ChildRequestFactories == null || step.ChildRequestFactories.Count == 0)
            return StepResult.Failed("No child requests configured for WhenAll", position);

        var childFlowIds = new List<string>();
        if (step.StartChildRequests != null)
        {
            foreach (var startChild in step.StartChildRequests)
            {
                await startChild(_mediator, state, cancellationToken);
                childFlowIds.Add(Guid.NewGuid().ToString("N")); // In real impl, get from request
            }
        }

        // Create wait condition
        var correlationId = BuildWaitConditionCorrelationId(state.FlowId!, position);
        var waitCondition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.All,
            ExpectedCount = step.ChildRequestCount,
            CompletedCount = 0,
            Timeout = step.Timeout ?? TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow,
            FlowId = state.FlowId!,
            FlowType = _config.GetType().FullName ?? _config.GetType().Name,
            Step = topLevelStepIndex,
            ChildFlowIds = childFlowIds
        };

        await _store.SetWaitConditionAsync(correlationId, waitCondition, cancellationToken);

        // Return suspended status
        return StepResult.Suspended(position);
    }

    private async Task<StepResult> ExecuteWhenAnyAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        CancellationToken cancellationToken)
    {
        var topLevelStepIndex = GetTopLevelStepIndex(position);

        // Start all child requests
        if (step.ChildRequestFactories == null || step.ChildRequestFactories.Count == 0)
            return StepResult.Failed("No child requests configured for WhenAny", position);

        var childFlowIds = new List<string>();
        if (step.StartChildRequests != null)
        {
            foreach (var startChild in step.StartChildRequests)
            {
                await startChild(_mediator, state, cancellationToken);
                childFlowIds.Add(Guid.NewGuid().ToString("N"));
            }
        }

        // Create wait condition
        var correlationId = BuildWaitConditionCorrelationId(state.FlowId!, position);
        var waitCondition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.Any,
            ExpectedCount = step.ChildRequestCount,
            CompletedCount = 0,
            Timeout = step.Timeout ?? TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow,
            FlowId = state.FlowId!,
            FlowType = _config.GetType().FullName ?? _config.GetType().Name,
            Step = topLevelStepIndex,
            CancelOthers = true,
            ChildFlowIds = childFlowIds
        };

        await _store.SetWaitConditionAsync(correlationId, waitCondition, cancellationToken);

        return StepResult.Suspended(position);
    }

    private async Task<StepResult> ExecuteDelayAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        CancellationToken cancellationToken)
    {
        var topLevelStepIndex = GetTopLevelStepIndex(position);

        if (_scheduler == null)
            return StepResult.Failed("No IFlowScheduler configured. Add UseQuartzScheduling() to enable delayed execution.", position);

        if (step.DelayDuration == null || step.DelayDuration.Value <= TimeSpan.Zero)
            return StepResult.Succeeded(position: position); // No delay, continue immediately

        var correlationId = BuildWaitConditionCorrelationId(state.FlowId!, position);
        var resumeAt = DateTimeOffset.UtcNow.Add(step.DelayDuration.Value);
        var scheduleId = await _scheduler.ScheduleResumeAsync(
            state.FlowId!,
            correlationId,
            resumeAt,
            cancellationToken);

        // Store schedule info for potential cancellation
        var waitCondition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = step.DelayDuration.Value.Add(TimeSpan.FromMinutes(1)),
            CreatedAt = DateTime.UtcNow,
            FlowId = state.FlowId!,
            FlowType = _config.GetType().FullName ?? _config.GetType().Name,
            Step = topLevelStepIndex,
            ScheduleId = scheduleId
        };

        await _store.SetWaitConditionAsync(correlationId, waitCondition, cancellationToken);

        return StepResult.Suspended(position);
    }

    private async Task<StepResult> ExecuteScheduleAtAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        CancellationToken cancellationToken)
    {
        var topLevelStepIndex = GetTopLevelStepIndex(position);

        if (_scheduler == null)
            return StepResult.Failed("No IFlowScheduler configured. Add UseQuartzScheduling() to enable scheduled execution.", position);

        if (step.GetScheduleTime == null)
            return StepResult.Failed("No schedule time selector configured for ScheduleAt step", position);

        var scheduleTime = step.GetScheduleTime(state);
        var resumeAt = new DateTimeOffset(scheduleTime, TimeSpan.Zero);

        // If schedule time is in the past, continue immediately
        if (resumeAt <= DateTimeOffset.UtcNow)
            return StepResult.Succeeded(position: position);

        var correlationId = BuildWaitConditionCorrelationId(state.FlowId!, position);
        var scheduleId = await _scheduler.ScheduleResumeAsync(
            state.FlowId!,
            correlationId,
            resumeAt,
            cancellationToken);

        // Store schedule info
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
            FlowType = _config.GetType().FullName ?? _config.GetType().Name,
            Step = topLevelStepIndex,
            ScheduleId = scheduleId
        };

        await _store.SetWaitConditionAsync(correlationId, waitCondition, cancellationToken);

        return StepResult.Suspended(position);
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

    private async Task PublishStepFailedAsync(
        TState state,
        int stepIndex,
        string? error,
        CancellationToken cancellationToken)
    {
        if (_config.OnStepFailedFactory == null)
            return;

        var failedEvent = _config.OnStepFailedFactory(state, stepIndex, error);
        await _mediator.PublishAsync(failedEvent, cancellationToken);
    }

    private async Task PublishFlowFailedAsync(
        TState state,
        string? error,
        CancellationToken cancellationToken)
    {
        if (_config.OnFlowFailedFactory == null)
            return;

        var failedEvent = _config.OnFlowFailedFactory(state, error);
        await _mediator.PublishAsync(failedEvent, cancellationToken);
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

    private async Task<FlowSnapshot<TState>> UpdateSnapshotAsync(
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

        if (await TryPersistSnapshotAsync(original, updated, cancellationToken))
            return updated;

        throw new InvalidOperationException(
            $"Failed to persist flow snapshot '{original.FlowId}' from version {original.Version} to {updated.Version}.");
    }

    private async Task<bool> TryPersistSnapshotAsync(
        FlowSnapshot<TState> original,
        FlowSnapshot<TState> nextVersionSnapshot,
        CancellationToken cancellationToken)
    {
        var versioning = _store as IDslFlowStoreVersioning;
        if (versioning != null)
        {
            var snapshotToPersist = versioning.VersioningMode == DslFlowStoreVersioningMode.StoreAdvancesVersion
                ? nextVersionSnapshot with { Version = original.Version }
                : nextVersionSnapshot;

            return await _store.UpdateAsync(snapshotToPersist, cancellationToken);
        }

        if (await _store.UpdateAsync(nextVersionSnapshot, cancellationToken))
            return true;

        var currentVersionSnapshot = nextVersionSnapshot with { Version = original.Version };
        return await _store.UpdateAsync(currentVersionSnapshot, cancellationToken);
    }

    // ========== Position Navigation - Branch Support ==========

    private FlowPosition StepToPosition(int stepIndex) => new([stepIndex]);

    private static int GetTopLevelStepIndex(FlowPosition position) => position.Path.Length > 0 ? position.Path[0] : 0;

    private static string BuildWaitConditionCorrelationId(string flowId, FlowPosition position)
    {
        if (position.Path.Length <= 1)
            return $"{flowId}-step-{GetTopLevelStepIndex(position)}";

        return $"{flowId}-step-{GetTopLevelStepIndex(position)}-path-{string.Join("-", position.Path.Skip(1))}";
    }

    private static bool IsSuspendingStepType(StepType stepType) => stepType is
        StepType.WhenAll or
        StepType.WhenAny or
        StepType.Delay or
        StepType.ScheduleAt;

    private FlowStep? FindFirstSuspendingStep(List<FlowStep>? steps)
    {
        if (steps == null)
            return null;

        foreach (var step in steps)
        {
            if (IsSuspendingStepType(step.Type))
                return step;

            var nested = step.Type switch
            {
                StepType.If => FindFirstSuspendingStep(step.ThenBranch)
                    ?? FindFirstSuspendingStep(step.ElseBranch)
                    ?? FindFirstSuspendingStep(step.ElseIfBranches?.SelectMany(branch => branch.Steps).ToList()),
                StepType.Switch => FindFirstSuspendingStep(step.Cases?.Values.SelectMany(branch => branch).ToList())
                    ?? FindFirstSuspendingStep(step.DefaultBranch),
                StepType.Parallel => FindFirstSuspendingStep(step.ParallelBranches?.SelectMany(branch => branch).ToList()),
                StepType.Throttle => FindFirstSuspendingStep(step.ThrottleSteps),
                _ => null
            };

            if (nested != null)
                return nested;
        }

        return null;
    }

    private static string BuildUnsupportedSuspendingStepMessage(string containerName, StepType unsupportedStepType)
        => $"{containerName} does not support suspending nested steps. Found {unsupportedStepType}.";

    private FlowStep? GetStepAtPosition(FlowPosition position)
    {
        if (position.Path.Length == 0)
            return null;

        if (position.Path[0] < 0 || position.Path[0] >= _config.Steps.Count)
            return null;

        FlowStep current = _config.Steps[position.Path[0]];
        int depth = 1;

        while (depth < position.Path.Length)
        {
            switch (current.Type)
            {
                case StepType.If:
                case StepType.Switch:
                case StepType.Parallel:
                {
                    var selector = position.Path[depth++];
                    var branchSteps = GetNestedStepsForSelector(current, selector);
                    if (branchSteps == null || depth >= position.Path.Length)
                        return null;

                    var stepIndex = position.Path[depth++];
                    if (stepIndex < 0 || stepIndex >= branchSteps.Count)
                        return null;

                    current = branchSteps[stepIndex];
                    break;
                }
                case StepType.Throttle:
                {
                    var scopedSteps = current.ThrottleSteps;
                    if (scopedSteps == null)
                        return null;

                    var stepIndex = position.Path[depth++];
                    if (stepIndex < 0 || stepIndex >= scopedSteps.Count)
                        return null;

                    current = scopedSteps[stepIndex];
                    break;
                }
                default:
                    return null;
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

    private List<FlowStep>? GetNestedStepsForSelector(FlowStep step, int selector)
        => step.Type switch
        {
            StepType.If or StepType.Switch => GetBranchAtPosition(step, selector),
            StepType.Parallel when step.ParallelBranches != null && selector >= 0 && selector < step.ParallelBranches.Count
                => step.ParallelBranches[selector],
            _ => null
        };

    private async Task<DslFlowResult<TState>> ResumeFromBranchPositionAsync(
        FlowSnapshot<TState> snapshot,
        bool resumeCurrentStep,
        CancellationToken cancellationToken)
    {
        var state = snapshot.State;
        var position = snapshot.Position;

        var stepIndex = position.Path[0]; // First element is the main step index
        if (stepIndex >= _config.Steps.Count)
        {
            return DslFlowResult<TState>.Failure(DslFlowStatus.Failed,
                $"Invalid step index {stepIndex} in position {string.Join(",", position.Path)}");
        }

        var step = _config.Steps[stepIndex];

        var result = step.Type switch
        {
            StepType.ForEach => await ResumeForEachAsync(state, step, position, stepIndex, cancellationToken),
            StepType.Throttle => await ResumeThrottleStepAsync(state, step, position, 0, resumeCurrentStep, cancellationToken),
            StepType.If or StepType.Switch => await ResumeBranchStepAsync(state, step, position, 0, resumeCurrentStep, cancellationToken),
            _ => await (resumeCurrentStep
                ? ExecuteStepAsync(state, step, StepToPosition(stepIndex), cancellationToken)
                : Task.FromResult(StepResult.Succeeded(position: StepToPosition(stepIndex))))
        };

        if (!result.Success)
        {
            await UpdateSnapshotAsync(snapshot, state, result.Position ?? position, DslFlowStatus.Failed, result.Error, cancellationToken);
            return DslFlowResult<TState>.Failure(state, DslFlowStatus.Failed, result.Error);
        }

        if (result.IsSuspended)
        {
            await UpdateSnapshotAsync(snapshot, state, result.Position ?? position, DslFlowStatus.Suspended, null, cancellationToken);
            return DslFlowResult<TState>.Success(state, DslFlowStatus.Suspended);
        }

        // Continue with remaining steps after the branch step
        return await ExecuteFromStepAsync(snapshot, stepIndex + 1, cancellationToken);
    }

    private async Task<StepResult> ResumeBranchStepAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        int pathIndex,
        bool resumeCurrentStep,
        CancellationToken cancellationToken)
    {
        if (step.Type is not StepType.If and not StepType.Switch)
        {
            var currentStepPosition = new FlowPosition(position.Path[..(pathIndex + 1)].ToArray());
            return resumeCurrentStep
                ? await ExecuteStepAsync(state, step, currentStepPosition, cancellationToken)
                : StepResult.Succeeded(position: currentStepPosition);
        }

        if (pathIndex + 1 >= position.Path.Length)
        {
            var currentStepPosition = new FlowPosition(position.Path[..(pathIndex + 1)].ToArray());
            return await ExecuteStepAsync(state, step, currentStepPosition, cancellationToken);
        }

        var branchIndex = position.Path[pathIndex + 1];
        var branchSteps = GetBranchAtPosition(step, branchIndex);
        if (branchSteps == null)
            return StepResult.Failed($"Invalid branch index {branchIndex} at position {string.Join(",", position.Path)}");

        var branchPosition = new FlowPosition(position.Path[..(pathIndex + 2)].ToArray());
        if (pathIndex + 2 >= position.Path.Length)
            return await ExecuteBranchStepsFromAsync(state, branchSteps, branchPosition, 0, cancellationToken);

        var branchStepIndex = position.Path[pathIndex + 2];
        if (branchStepIndex < 0 || branchStepIndex >= branchSteps.Count)
            return StepResult.Failed($"Invalid branch step index {branchStepIndex} at position {string.Join(",", position.Path)}");

        var currentBranchStep = branchSteps[branchStepIndex];
        StepResult currentResult;

        if (pathIndex + 3 < position.Path.Length && currentBranchStep.Type is StepType.If or StepType.Switch or StepType.Throttle)
        {
            currentResult = await ResumeNestedContainerStepAsync(
                state,
                currentBranchStep,
                position,
                pathIndex + 2,
                resumeCurrentStep,
                cancellationToken);
        }
        else if (resumeCurrentStep)
        {
            var currentBranchStepPosition = new FlowPosition(position.Path[..(pathIndex + 3)].ToArray());
            currentResult = await ExecuteStepAsync(state, currentBranchStep, currentBranchStepPosition, cancellationToken);

            if (currentResult.Success && currentResult.Result != null && currentBranchStep.SetResult != null)
                currentBranchStep.SetResult(state, currentResult.Result);
        }
        else
        {
            currentResult = StepResult.Succeeded(position: new FlowPosition(position.Path[..(pathIndex + 3)].ToArray()));
        }

        if (currentResult.IsSuspended || (!currentResult.Success && !currentResult.Skipped))
            return currentResult;

        return await ExecuteBranchStepsFromAsync(state, branchSteps, branchPosition, branchStepIndex + 1, cancellationToken);
    }

    private Task<StepResult> ResumeNestedContainerStepAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        int pathIndex,
        bool resumeCurrentStep,
        CancellationToken cancellationToken)
        => step.Type switch
        {
            StepType.If or StepType.Switch => ResumeBranchStepAsync(state, step, position, pathIndex, resumeCurrentStep, cancellationToken),
            StepType.Throttle => ResumeThrottleStepAsync(state, step, position, pathIndex, resumeCurrentStep, cancellationToken),
            _ => Task.FromResult(StepResult.Failed($"Unsupported nested container type: {step.Type}"))
        };

    private async Task<StepResult> ResumeThrottleStepAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        int pathIndex,
        bool resumeCurrentStep,
        CancellationToken cancellationToken)
    {
        if (step.ThrottleSteps == null)
            return StepResult.Failed($"Throttle step has no inner steps at position {string.Join(",", position.Path)}");

        return await ResumeScopedStepsAsync(
            state,
            step.ThrottleSteps,
            position,
            pathIndex,
            resumeCurrentStep,
            cancellationToken);
    }

    private async Task<StepResult> ResumeScopedStepsAsync(
        TState state,
        List<FlowStep> steps,
        FlowPosition position,
        int pathIndex,
        bool resumeCurrentStep,
        CancellationToken cancellationToken)
    {
        var scopePosition = new FlowPosition(position.Path[..(pathIndex + 1)].ToArray());
        if (pathIndex + 1 >= position.Path.Length)
            return await ExecuteBranchStepsFromAsync(state, steps, scopePosition, 0, cancellationToken);

        var stepIndex = position.Path[pathIndex + 1];
        if (stepIndex < 0 || stepIndex >= steps.Count)
            return StepResult.Failed($"Invalid scoped step index {stepIndex} at position {string.Join(",", position.Path)}");

        var currentStep = steps[stepIndex];
        StepResult currentResult;

        if (pathIndex + 2 < position.Path.Length && currentStep.Type is StepType.If or StepType.Switch or StepType.Throttle)
        {
            currentResult = await ResumeNestedContainerStepAsync(
                state,
                currentStep,
                position,
                pathIndex + 1,
                resumeCurrentStep,
                cancellationToken);
        }
        else if (resumeCurrentStep)
        {
            var currentStepPosition = new FlowPosition(position.Path[..(pathIndex + 2)].ToArray());
            currentResult = await ExecuteStepAsync(state, currentStep, currentStepPosition, cancellationToken);

            if (currentResult.Success && currentResult.Result != null && currentStep.SetResult != null)
                currentStep.SetResult(state, currentResult.Result);
        }
        else
        {
            currentResult = StepResult.Succeeded(position: new FlowPosition(position.Path[..(pathIndex + 2)].ToArray()));
        }

        if (currentResult.IsSuspended || (!currentResult.Success && !currentResult.Skipped))
            return currentResult;

        return await ExecuteBranchStepsFromAsync(state, steps, scopePosition, stepIndex + 1, cancellationToken);
    }

    private record struct ExecutedStep(int Index, FlowStep Step);

    private readonly struct StepResult
    {
        public bool Success { get; }
        public bool Skipped { get; }
        public string? Error { get; }
        public object? Result { get; }
        public bool IsSuspended { get; }
        public FlowPosition? Position { get; }

        private StepResult(bool success, bool skipped, bool suspended, string? error, object? result, FlowPosition? position)
        {
            Success = success;
            Skipped = skipped;
            IsSuspended = suspended;
            Error = error;
            Result = result;
            Position = position;
        }

        public static StepResult Succeeded(object? result = null, FlowPosition? position = null) => new(true, false, false, null, result, position);
        public static StepResult Failed(string error, FlowPosition? position = null) => new(false, false, false, error, null, position);
        public static StepResult Skip(FlowPosition? position = null) => new(true, true, false, null, null, position);
        public static StepResult Suspended(FlowPosition? position = null) => new(true, false, true, null, null, position);
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
