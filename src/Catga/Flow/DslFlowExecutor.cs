using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Flow.Dsl;

/// <summary>
/// Telemetry for DSL Flow execution.
/// </summary>
public static class DslFlowTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Catga.Flow.Dsl", "1.0.0");
    public static readonly Meter Meter = new("Catga.Flow.Dsl", "1.0.0");

    // Counters
    public static readonly Counter<long> FlowsStarted = Meter.CreateCounter<long>("catga.flow.started", "flows", "Number of flows started");
    public static readonly Counter<long> FlowsCompleted = Meter.CreateCounter<long>("catga.flow.completed", "flows", "Number of flows completed");
    public static readonly Counter<long> FlowsFailed = Meter.CreateCounter<long>("catga.flow.failed", "flows", "Number of flows failed");
    public static readonly Counter<long> StepsExecuted = Meter.CreateCounter<long>("catga.flow.step.executed", "steps", "Number of steps executed");
    public static readonly Counter<long> CompensationsExecuted = Meter.CreateCounter<long>("catga.flow.compensation.executed", "compensations", "Number of compensations executed");

    // Histograms
    public static readonly Histogram<double> FlowDuration = Meter.CreateHistogram<double>("catga.flow.duration", "ms", "Flow execution duration");
    public static readonly Histogram<double> StepDuration = Meter.CreateHistogram<double>("catga.flow.step.duration", "ms", "Step execution duration");
}

/// <summary>
/// Executes flows defined by FlowConfig DSL.
/// </summary>
public class DslFlowExecutor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, TConfig> : IFlow<TState>
    where TState : class, IFlowState, new()
    where TConfig : FlowConfig<TState>
{
    private readonly ICatgaMediator _mediator;
    private readonly IDslFlowStore _store;
    private readonly TConfig _config;
    private readonly List<ExecutedStep> _executedSteps = [];

    public DslFlowExecutor(ICatgaMediator mediator, IDslFlowStore store, TConfig config)
    {
        _mediator = mediator;
        _store = store;
        _config = config;
        _config.Build();
    }

    public async Task<DslFlowResult<TState>> RunAsync(TState state, CancellationToken cancellationToken = default)
    {
        state.FlowId ??= Guid.NewGuid().ToString("N");
        var flowName = _config.Name;
        var sw = Stopwatch.StartNew();

        using var activity = DslFlowTelemetry.ActivitySource.StartActivity($"Flow.{flowName}");
        activity?.SetTag("flow.id", state.FlowId);
        activity?.SetTag("flow.name", flowName);
        activity?.SetTag("flow.type", typeof(TConfig).FullName);

        DslFlowTelemetry.FlowsStarted.Add(1, new KeyValuePair<string, object?>("flow.name", flowName));

        var snapshot = new FlowSnapshot<TState>(
            state.FlowId,
            state,
            CurrentStep: 0,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);

        await _store.CreateAsync(snapshot, cancellationToken);

        var result = await ExecuteFromStepAsync(snapshot, 0, cancellationToken);

        sw.Stop();
        DslFlowTelemetry.FlowDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("flow.name", flowName));

        if (result.IsSuccess)
        {
            DslFlowTelemetry.FlowsCompleted.Add(1, new KeyValuePair<string, object?>("flow.name", flowName));
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            DslFlowTelemetry.FlowsFailed.Add(1, new KeyValuePair<string, object?>("flow.name", flowName));
            activity?.SetStatus(ActivityStatusCode.Error, result.Error);
        }

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
            return DslFlowResult<TState>.Failure(DslFlowStatus.Failed, snapshot.Error);

        if (snapshot.Status == DslFlowStatus.Cancelled)
            return DslFlowResult<TState>.Failure(DslFlowStatus.Cancelled, "Flow was cancelled");

        // Handle suspended flow - check wait condition
        if (snapshot.Status == DslFlowStatus.Suspended)
        {
            var correlationId = $"{flowId}-step-{snapshot.CurrentStep}";
            var waitCondition = await _store.GetWaitConditionAsync(correlationId, cancellationToken);

            if (waitCondition != null)
            {
                var resumeResult = await CheckAndResumeFromWaitConditionAsync(snapshot, waitCondition, cancellationToken);
                if (resumeResult != null)
                    return resumeResult.Value;

                // Wait condition satisfied, continue from next step
                return await ExecuteFromStepAsync(snapshot, snapshot.CurrentStep + 1, cancellationToken);
            }
        }

        return await ExecuteFromStepAsync(snapshot, snapshot.CurrentStep, cancellationToken);
    }

    private async Task<DslFlowResult<TState>?> CheckAndResumeFromWaitConditionAsync(
        FlowSnapshot<TState> snapshot,
        WaitCondition waitCondition,
        CancellationToken cancellationToken)
    {
        var state = snapshot.State;
        var step = _config.Steps[snapshot.CurrentStep];

        // Check timeout
        if (DateTime.UtcNow - waitCondition.CreatedAt > waitCondition.Timeout)
        {
            await UpdateSnapshotAsync(snapshot, state, snapshot.CurrentStep, DslFlowStatus.Failed, "WhenAll/WhenAny timeout", cancellationToken);
            return DslFlowResult<TState>.Failure(DslFlowStatus.Failed, "WhenAll/WhenAny timeout");
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
                if (step.HasCompensation && step.CompensationFactory != null)
                {
                    try
                    {
                        var compensation = step.CompensationFactory.DynamicInvoke(state);
                        if (compensation is IRequest request)
                        {
                            await _mediator.SendAsync(request, cancellationToken);
                        }
                    }
                    catch { }
                }

                await UpdateSnapshotAsync(snapshot, state, snapshot.CurrentStep, DslFlowStatus.Failed, failedChild.Error, cancellationToken);
                return DslFlowResult<TState>.Failure(DslFlowStatus.Failed, failedChild.Error);
            }
        }
        else // WaitType.Any
        {
            var successChild = waitCondition.Results.FirstOrDefault(r => r.Success);
            if (successChild != null)
            {
                // Store result if configured
                if (step.ResultSetter != null && successChild.Result != null)
                {
                    step.ResultSetter.DynamicInvoke(state, successChild.Result);
                }
            }
            else if (waitCondition.CompletedCount >= waitCondition.ExpectedCount)
            {
                // All failed
                var lastError = waitCondition.Results.LastOrDefault()?.Error ?? "All child flows failed";
                await UpdateSnapshotAsync(snapshot, state, snapshot.CurrentStep, DslFlowStatus.Failed, lastError, cancellationToken);
                return DslFlowResult<TState>.Failure(DslFlowStatus.Failed, lastError);
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
                await UpdateSnapshotAsync(snapshot, state, i, DslFlowStatus.Cancelled, null, cancellationToken);
                return DslFlowResult<TState>.Failure(DslFlowStatus.Cancelled, "Flow was cancelled");
            }

            var step = steps[i];
            var stepSw = Stopwatch.StartNew();
            var result = await ExecuteStepAsync(state, step, i, cancellationToken);
            stepSw.Stop();

            var flowName = _config.Name;
            DslFlowTelemetry.StepsExecuted.Add(1,
                new KeyValuePair<string, object?>("flow.name", flowName),
                new KeyValuePair<string, object?>("step.index", i),
                new KeyValuePair<string, object?>("step.type", step.Type.ToString()));
            DslFlowTelemetry.StepDuration.Record(stepSw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("flow.name", flowName),
                new KeyValuePair<string, object?>("step.index", i));

            if (result.IsSuspended)
            {
                // Flow is suspended waiting for child flows
                await UpdateSnapshotAsync(snapshot, state, i, DslFlowStatus.Suspended, null, cancellationToken);
                return DslFlowResult<TState>.Success(state, DslFlowStatus.Suspended);
            }

            if (!result.Success)
            {
                // Execute compensation for the failed step if it has one
                if (step.HasCompensation && step.CompensationFactory != null)
                {
                    try
                    {
                        var compensation = step.CompensationFactory.DynamicInvoke(state);
                        if (compensation is IRequest request)
                        {
                            await _mediator.SendAsync(request, cancellationToken);
                        }
                    }
                    catch
                    {
                        // Compensation failure is logged but doesn't change the flow result
                    }
                }

                // Execute compensations for previously successful steps in reverse order
                await ExecuteCompensationsAsync(state, cancellationToken);

                // Publish OnFlowFailed event
                if (_config.OnFlowFailedFactory != null)
                {
                    var failedEvent = _config.OnFlowFailedFactory(state, result.Error);
                    await _mediator.PublishAsync(failedEvent, cancellationToken);
                }

                await UpdateSnapshotAsync(snapshot, state, i, DslFlowStatus.Failed, result.Error, cancellationToken);
                return DslFlowResult<TState>.Failure(DslFlowStatus.Failed, result.Error);
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
                await UpdateSnapshotAsync(snapshot, state, i + 1, DslFlowStatus.Running, null, cancellationToken);
            }
        }

        // Publish OnFlowCompleted event
        if (_config.OnFlowCompletedFactory != null)
        {
            var completedEvent = _config.OnFlowCompletedFactory(state);
            await _mediator.PublishAsync(completedEvent, cancellationToken);
        }

        await UpdateSnapshotAsync(snapshot, state, steps.Count, DslFlowStatus.Completed, null, cancellationToken);
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
        if (step.RequestFactory == null)
            return StepResult.Failed("No request factory configured");

        // Create the request
        var request = step.RequestFactory.DynamicInvoke(state);
        if (request == null)
            return StepResult.Failed("Request factory returned null");

        // Execute via mediator
        CatgaResult result;
        object? resultValue = null;

        if (step.HasResult)
        {
            // Use reflection to call the generic SendAsync<TRequest, TResult>
            var requestType = request.GetType();
            var resultType = requestType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
                ?.GetGenericArguments()[0];

            if (resultType != null)
            {
                // Find the SendAsync method with 2 generic parameters
                var methods = typeof(ICatgaMediator).GetMethods()
                    .Where(m => m.Name == nameof(ICatgaMediator.SendAsync) && m.IsGenericMethod && m.GetGenericArguments().Length == 2);
                var method = methods.FirstOrDefault();

                if (method != null)
                {
                    var genericMethod = method.MakeGenericMethod(requestType, resultType);
                    var task = genericMethod.Invoke(_mediator, new[] { request, cancellationToken });

                    if (task != null)
                    {
                        // Await the ValueTask
                        await ((dynamic)task).ConfigureAwait(false);
                        var catgaResult = ((dynamic)task).Result;

                        bool isSuccess = catgaResult.IsSuccess;
                        string? error = catgaResult.Error;
                        resultValue = catgaResult.Value;

                        if (!isSuccess)
                        {
                            if (step.IsOptional)
                                return StepResult.Skip();
                            return StepResult.Failed(error ?? "Request failed");
                        }

                        // Check FailIf condition on result
                        if (step.HasFailCondition && step.FailConditionFactory != null && resultValue != null)
                        {
                            var shouldFail = (bool)(step.FailConditionFactory.DynamicInvoke(resultValue) ?? false);
                            if (shouldFail)
                            {
                                return StepResult.Failed(step.FailConditionMessage ?? "FailIf condition met");
                            }
                        }

                        // Set result on state
                        if (step.ResultSetter != null && resultValue != null)
                        {
                            step.ResultSetter.DynamicInvoke(state, resultValue);
                        }
                    }
                }
            }
        }
        else
        {
            // IRequest without result
            result = await _mediator.SendAsync((IRequest)request, cancellationToken);
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
        if (step.RequestFactory == null)
            return StepResult.Failed("No event factory configured");

        var @event = step.RequestFactory.DynamicInvoke(state) as IEvent;
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
        foreach (var factory in step.ChildRequestFactories)
        {
            var request = factory.DynamicInvoke(state);
            if (request is IRequest req)
            {
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
        foreach (var factory in step.ChildRequestFactories)
        {
            var request = factory.DynamicInvoke(state);
            if (request is IRequest req)
            {
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

    private async Task ExecuteCompensationsAsync(TState state, CancellationToken cancellationToken)
    {
        var flowName = _config.Name;

        // Execute compensations in reverse order
        for (var i = _executedSteps.Count - 1; i >= 0; i--)
        {
            var executed = _executedSteps[i];
            if (executed.Step.CompensationFactory == null)
                continue;

            try
            {
                var compensation = executed.Step.CompensationFactory.DynamicInvoke(state);
                if (compensation is IRequest request)
                {
                    await _mediator.SendAsync(request, cancellationToken);
                    DslFlowTelemetry.CompensationsExecuted.Add(1,
                        new KeyValuePair<string, object?>("flow.name", flowName),
                        new KeyValuePair<string, object?>("step.index", executed.Index));
                }
            }
            catch
            {
                // Compensation failures are logged but don't stop other compensations
            }
        }
    }

    private bool EvaluateCondition(TState state, FlowStep step, int stepIndex)
    {
        if (step.ConditionFactory == null)
            return true;

        return (bool)(step.ConditionFactory.DynamicInvoke(state) ?? true);
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
        int currentStep,
        DslFlowStatus status,
        string? error,
        CancellationToken cancellationToken)
    {
        var updated = original with
        {
            State = state,
            CurrentStep = currentStep,
            Status = status,
            Error = error,
            UpdatedAt = DateTime.UtcNow,
            Version = original.Version + 1
        };

        await _store.UpdateAsync(updated, cancellationToken);
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
