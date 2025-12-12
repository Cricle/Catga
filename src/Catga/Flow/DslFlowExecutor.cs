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
public partial class DslFlowExecutor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState, TConfig> : IFlow<TState>
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
                if (step.ResultSetter != null && successChild.Result != null)
                {
                    step.ResultSetter.DynamicInvoke(state, successChild.Result);
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
                await UpdateSnapshotAsync(snapshot, state, StepToPosition(i), DslFlowStatus.Suspended, null, cancellationToken);
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
                StepType.While => await ExecuteWhileAsync(state, step, stepIndex, cancellationToken),
                StepType.DoWhile => await ExecuteDoWhileAsync(state, step, stepIndex, cancellationToken),
                StepType.Repeat => await ExecuteRepeatAsync(state, step, stepIndex, cancellationToken),
                StepType.Try => await ExecuteTryAsync(state, step, stepIndex, cancellationToken),
                StepType.CallFlow => await ExecuteCallFlowAsync(state, step, stepIndex, cancellationToken),
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

        // Create the request using compiled step if available, otherwise fallback to DynamicInvoke
        object? request;
        if (step.CompiledStep != null)
        {
            request = step.CompiledStep.ExecuteRequestFactory(state);
        }
        else
        {
            request = step.RequestFactory.DynamicInvoke(state);
        }

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
                        // Use reflection to await and get result instead of dynamic
                        var taskType = task.GetType();

                        // Get ConfigureAwait method
                        var configureAwaitMethod = taskType.GetMethod("ConfigureAwait",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        var configuredTask = configureAwaitMethod?.Invoke(task, new object[] { false });

                        // Get GetAwaiter method
                        var getAwaiterMethod = configuredTask?.GetType().GetMethod("GetAwaiter",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                        var awaiter = getAwaiterMethod?.Invoke(configuredTask, Array.Empty<object>());

                        // Get IsCompleted property
                        var isCompletedProp = awaiter?.GetType().GetProperty("IsCompleted",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

                        // Get GetResult method
                        var getResultMethod = awaiter?.GetType().GetMethod("GetResult",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

                        // For now, use a simpler approach: cast to ValueTask and handle
                        try
                        {
                            // Try to get Result property directly (works for Task<T>)
                            var resultProp = taskType.GetProperty("Result",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

                            if (resultProp != null)
                            {
                                // Wait for task to complete
                                var waitMethod = taskType.GetMethod("Wait", System.Type.EmptyTypes);
                                waitMethod?.Invoke(task, Array.Empty<object>());

                                var catgaResult = resultProp.GetValue(task);
                                if (catgaResult != null)
                                {
                                    var resultType_obj = catgaResult.GetType();
                                    bool isSuccess = (bool)(resultType_obj.GetProperty("IsSuccess")?.GetValue(catgaResult) ?? false);
                                    string? error = (string?)(resultType_obj.GetProperty("Error")?.GetValue(catgaResult));
                                    resultValue = resultType_obj.GetProperty("Value")?.GetValue(catgaResult);

                                    if (!isSuccess)
                                    {
                                        if (step.IsOptional)
                                            return StepResult.Skip();
                                        return StepResult.Failed(error ?? "Request failed");
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Fallback: reflection failed, return error
                            return StepResult.Failed("Failed to invoke SendAsync");
                        }

                        // Check FailIf condition on result
                        if (step.HasFailCondition && resultValue != null)
                        {
                            bool shouldFail;
                            if (step.CompiledStep != null)
                            {
                                shouldFail = step.CompiledStep.ExecuteFailCondition(resultValue);
                            }
                            else if (step.FailConditionFactory != null)
                            {
                                shouldFail = (bool)(step.FailConditionFactory.DynamicInvoke(resultValue) ?? false);
                            }
                            else
                            {
                                shouldFail = false;
                            }

                            if (shouldFail)
                            {
                                return StepResult.Failed(step.FailConditionMessage ?? "FailIf condition met");
                            }
                        }

                        // Set result on state
                        if (resultValue != null)
                        {
                            if (step.CompiledStep != null)
                            {
                                step.CompiledStep.ExecuteResultSetter(state, resultValue);
                            }
                            else if (step.ResultSetter != null)
                            {
                                step.ResultSetter.DynamicInvoke(state, resultValue);
                            }
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
                object? compensation;
                if (executed.Step.CompiledStep != null)
                {
                    compensation = executed.Step.CompiledStep.ExecuteCompensation(state);
                }
                else
                {
                    compensation = executed.Step.CompensationFactory.DynamicInvoke(state);
                }

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

        if (step.CompiledStep != null)
        {
            return step.CompiledStep.ExecuteCondition(state);
        }
        else
        {
            return (bool)(step.ConditionFactory.DynamicInvoke(state) ?? true);
        }
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

    private async Task<StepResult> ExecuteIfAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        // Evaluate the If condition
        if (step.BranchCondition == null)
            return StepResult.Failed("If step has no condition");

        bool conditionResult;
        if (step.CompiledStep != null)
        {
            conditionResult = step.CompiledStep.ExecuteBranchCondition(state);
        }
        else
        {
            var condition = (Func<TState, bool>)step.BranchCondition;
            conditionResult = condition(state);
        }

        // Select the appropriate branch and branch index
        List<FlowStep>? branchToExecute = null;
        int branchIndex = 0; // 0 = Then, 1+ = ElseIf, -1 = Else

        if (conditionResult)
        {
            branchToExecute = step.ThenBranch;
            branchIndex = 0;
        }
        else if (step.ElseIfBranches != null)
        {
            // Check ElseIf branches
            int elseIfIndex = 1;
            foreach (var (elseIfCondition, elseIfBranch)in step.ElseIfBranches)
            {
                bool elseIfResult;
                if (step.CompiledStep != null)
                {
                    // For ElseIf, we need to evaluate the condition from the tuple
                    // This is a limitation - we can't use compiled step for ElseIf conditions
                    var elseIfFunc = (Func<TState, bool>)elseIfCondition;
                    elseIfResult = elseIfFunc(state);
                }
                else
                {
                    var elseIfFunc = (Func<TState, bool>)elseIfCondition;
                    elseIfResult = elseIfFunc(state);
                }

                if (elseIfResult)
                {
                    branchToExecute = elseIfBranch;
                    branchIndex = elseIfIndex;
                    break;
                }
                elseIfIndex++;
            }
        }

        // Fall through to Else if no condition matched
        if (branchToExecute == null && step.ElseBranch != null)
        {
            branchToExecute = step.ElseBranch;
            branchIndex = -1;
        }

        // Execute the selected branch with position tracking
        if (branchToExecute != null && branchToExecute.Count > 0)
        {
            var branchPosition = new FlowPosition([stepIndex, branchIndex]);
            var result = await ExecuteBranchStepsAsync(state, branchToExecute, branchPosition, cancellationToken);
            if (!result.Success)
                return result;
        }

        return StepResult.Succeeded();
    }

    private async Task<StepResult> ExecuteSwitchAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        // Evaluate the Switch selector
        if (step.SwitchSelector == null)
            return StepResult.Failed("Switch step has no selector");

        object? selectorValue;
        if (step.CompiledStep != null)
        {
            selectorValue = step.CompiledStep.ExecuteSwitchSelector(state);
        }
        else
        {
            selectorValue = step.SwitchSelector.DynamicInvoke(state);
        }

        // Find matching case and case index
        List<FlowStep>? branchToExecute = null;
        int caseIndex = -1; // -1 = Default

        if (step.Cases != null && selectorValue != null)
        {
            int idx = 0;
            foreach (var (caseValue, caseBranch) in step.Cases)
            {
                if (Equals(caseValue, selectorValue))
                {
                    branchToExecute = caseBranch;
                    caseIndex = idx;
                    break;
                }
                idx++;
            }
        }

        // Fall through to Default if no case matched
        if (branchToExecute == null && step.DefaultBranch != null)
        {
            branchToExecute = step.DefaultBranch;
            caseIndex = -1;
        }

        // Execute the selected branch with position tracking
        if (branchToExecute != null && branchToExecute.Count > 0)
        {
            var branchPosition = new FlowPosition([stepIndex, caseIndex]);
            var result = await ExecuteBranchStepsAsync(state, branchToExecute, branchPosition, cancellationToken);
            if (!result.Success)
                return result;
        }

        return StepResult.Succeeded();
    }

    private async Task<StepResult> ExecuteBranchStepsAsync(
        TState state,
        List<FlowStep> steps,
        FlowPosition parentPosition,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var branchStep = steps[i];
            // Create nested position: parent path + current step index
            var nestedPosition = parentPosition.EnterBranch(i);
            var result = await ExecuteStepAsync(state, branchStep, i, cancellationToken);

            if (result.IsSuspended)
                return result;

            if (!result.Success && !result.Skipped)
                return result;

            // Apply result to state if step has result setter
            if (result.Success && result.Result != null && branchStep.ResultSetter != null)
            {
                try
                {
                    branchStep.ResultSetter.DynamicInvoke(state, result.Result);
                }
                catch
                {
                    // Ignore setter errors
                }
            }
        }

        return StepResult.Succeeded();
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

    // Helper to convert step index to position for backward compatibility
    private FlowPosition StepToPosition(int stepIndex) => new([stepIndex]);

    /// <summary>
    /// Get the step at a given position, navigating through branches.
    /// </summary>
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

        var parentPosition = new FlowPosition(position.Path[..^1]);
        var step = GetStepAtPosition(new FlowPosition(position.Path[..^2]));

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

    private async Task<StepResult> ExecuteForEachAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.CollectionSelector == null)
            return StepResult.Failed("No collection selector configured for ForEach");

        try
        {
            // Get the collection to iterate over
            System.Collections.IEnumerable? collection;
            if (step.CompiledStep != null)
            {
                collection = step.CompiledStep.ExecuteCollectionSelector(state);
            }
            else
            {
                collection = step.CollectionSelector.DynamicInvoke(state) as System.Collections.IEnumerable;
            }

            if (collection == null)
                return StepResult.Succeeded(); // Empty collection, nothing to process

            // Convert to enumerable
            if (collection is not System.Collections.IEnumerable enumerable)
                return StepResult.Failed("Collection selector did not return an enumerable");

            // Check if streaming is enabled for memory optimization
            if (step.StreamingEnabled)
            {
                // Stream processing - don't materialize the entire collection
                return await ProcessItemsStreaming(state, step, enumerable, cancellationToken);
            }

            var items = enumerable.Cast<object>().ToList();
            if (items.Count == 0)
            {
                // Execute OnComplete callback even for empty collections
                if (step.CompiledStep != null)
                {
                    step.CompiledStep.ExecuteOnComplete(state);
                }
                else if (step.OnComplete != null)
                {
                    try
                    {
                        step.OnComplete.DynamicInvoke(state);
                    }
                    catch
                    {
                        // Ignore callback errors
                    }
                }
                return StepResult.Succeeded(); // Empty collection
            }

            // Determine parallelism
            var maxDegreeOfParallelism = step.MaxDegreeOfParallelism ?? 1;

            if (maxDegreeOfParallelism <= 1)
            {
                // Sequential processing
                return await ProcessItemsSequentially(state, step, items, cancellationToken);
            }
            else
            {
                // Parallel processing
                return await ProcessItemsInParallel(state, step, items, maxDegreeOfParallelism, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"ForEach execution failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ProcessItemsSequentially(
        TState state,
        FlowStep step,
        List<object> items,
        CancellationToken cancellationToken)
    {
        // Process each item sequentially
        foreach (var (item, index) in items.Select((item, index) => (item, index)))
        {
            var result = await ProcessSingleItemAsync(state, step, item, index, cancellationToken);
            if (!result.Success && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
            {
                return result;
            }
        }

        // Execute OnComplete callback if present
        if (step.CompiledStep != null)
        {
            step.CompiledStep.ExecuteOnComplete(state);
        }
        else if (step.OnComplete != null)
        {
            try
            {
                step.OnComplete.DynamicInvoke(state);
            }
            catch
            {
                // Ignore callback errors
            }
        }

        return StepResult.Succeeded();
    }

    private async Task<StepResult> ProcessItemsInParallel(
        TState state,
        FlowStep step,
        List<object> items,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);
        var tasks = new List<Task<StepResult>>();

        // Create tasks for parallel processing
        foreach (var (item, index) in items.Select((item, index) => (item, index)))
        {
            var task = ProcessSingleItemWithSemaphoreAsync(semaphore, state, step, item, index, cancellationToken);
            tasks.Add(task);
        }

        // Wait for all tasks to complete
        var results = await Task.WhenAll(tasks);

        // Check for failures
        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Any() && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
        {
            return failures.First();
        }

        // Execute OnComplete callback if present
        if (step.CompiledStep != null)
        {
            step.CompiledStep.ExecuteOnComplete(state);
        }
        else if (step.OnComplete != null)
        {
            try
            {
                step.OnComplete.DynamicInvoke(state);
            }
            catch
            {
                // Ignore callback errors
            }
        }

        return StepResult.Succeeded();
    }

    private async Task<StepResult> ProcessSingleItemWithSemaphoreAsync(
        SemaphoreSlim semaphore,
        TState state,
        FlowStep step,
        object item,
        int index,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await ProcessSingleItemAsync(state, step, item, index, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<StepResult> ProcessSingleItemAsync(
        TState state,
        FlowStep step,
        object item,
        int index,
        CancellationToken cancellationToken)
    {
        try
        {
            // Execute ItemStepsConfigurator if available
            if (step.ItemStepsConfigurator != null)
            {
                // Create a temporary flow builder to capture configured steps
                var tempBuilder = new FlowBuilder<TState>();
                step.ItemStepsConfigurator.DynamicInvoke(item, tempBuilder);

                // Execute the configured steps
                foreach (var configuredStep in tempBuilder.Steps)
                {
                    var stepResult = await ExecuteStepAsync(state, configuredStep, index, cancellationToken);
                    if (!stepResult.Success)
                    {
                        // Handle failure
                        if (step.CompiledStep != null)
                        {
                            step.CompiledStep.ExecuteOnItemFail(state, item, stepResult.Error);
                        }
                        else if (step.OnItemFail != null)
                        {
                            try
                            {
                                step.OnItemFail.DynamicInvoke(state, item, stepResult.Error);
                            }
                            catch
                            {
                                // Ignore callback errors
                            }
                        }
                        return StepResult.Failed($"ForEach failed on item {index}: {stepResult.Error}");
                    }

                    // Apply result to state if step has result setter (from .Into())
                    if (stepResult.Success && stepResult.Result != null && configuredStep.ResultSetter != null)
                    {
                        try
                        {
                            if (configuredStep.CompiledStep != null)
                            {
                                configuredStep.CompiledStep.ExecuteResultSetter(state, stepResult.Result);
                            }
                            else
                            {
                                configuredStep.ResultSetter.DynamicInvoke(state, stepResult.Result);
                            }
                        }
                        catch (Exception)
                        {
                            // Log the error but continue processing
                            // In production, this should use proper logging
                        }
                    }

                    // Apply result using OnItemSuccess callback if available
                    if (stepResult.Success)
                    {
                        if (step.CompiledStep != null)
                        {
                            step.CompiledStep.ExecuteOnItemSuccess(state, item, stepResult.Result);
                        }
                        else if (step.OnItemSuccess != null)
                        {
                            try
                            {
                                step.OnItemSuccess.DynamicInvoke(state, item, stepResult.Result);
                            }
                            catch
                            {
                                // Ignore callback errors
                            }
                        }
                    }
                }
            }

            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            // Handle item processing failure
            if (step.CompiledStep != null)
            {
                step.CompiledStep.ExecuteOnItemFail(state, item, ex.Message);
            }
            else if (step.OnItemFail != null)
            {
                try
                {
                    step.OnItemFail.DynamicInvoke(state, item, ex.Message);
                }
                catch
                {
                    // Ignore callback errors
                }
            }

            return StepResult.Failed($"ForEach failed on item {index}: {ex.Message}");
        }
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
        // Handle different step types for resumption
        return step.Type switch
        {
            StepType.ForEach => await ResumeForEachAsync(state, step, position, stepIndex, cancellationToken),
            StepType.If or StepType.Switch => await ExecuteStepAsync(state, step, stepIndex, cancellationToken),
            _ => await ExecuteStepAsync(state, step, stepIndex, cancellationToken)
        };
    }

    private async Task<StepResult> ResumeForEachAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.CollectionSelector == null)
            return StepResult.Failed("No collection selector configured for ForEach");

        try
        {
            // Get the collection to iterate over
            var collection = step.CollectionSelector.DynamicInvoke(state);
            if (collection == null)
                return StepResult.Succeeded(); // Empty collection, nothing to process

            // Convert to enumerable
            if (collection is not System.Collections.IEnumerable enumerable)
                return StepResult.Failed("Collection selector did not return an enumerable");

            var items = enumerable.Cast<object>().ToList();
            if (items.Count == 0)
                return StepResult.Succeeded(); // Empty collection

            // Get ForEach progress to determine where to resume
            var flowId = state.FlowId ?? throw new InvalidOperationException("FlowId is required for ForEach recovery");
            var progress = await _store.GetForEachProgressAsync(flowId, stepIndex, cancellationToken);
            if (progress == null)
            {
                // No progress found, start from the beginning
                return await ExecuteForEachAsync(state, step, stepIndex, cancellationToken);
            }

            // Resume from the current index in the progress
            var startIndex = progress.CurrentIndex;
            if (startIndex >= items.Count)
            {
                // All items already processed
                if (step.OnComplete != null)
                {
                    try
                    {
                        step.OnComplete.DynamicInvoke(state);
                    }
                    catch
                    {
                        // Ignore callback errors
                    }
                }
                return StepResult.Succeeded();
            }

            // Process remaining items
            var remainingItems = items.Skip(startIndex).ToList();
            var maxDegreeOfParallelism = step.MaxDegreeOfParallelism ?? 1;

            if (maxDegreeOfParallelism <= 1)
            {
                // Sequential processing from resume point
                return await ProcessItemsSequentiallyFromIndex(state, step, items, startIndex, cancellationToken);
            }
            else
            {
                // Parallel processing from resume point
                return await ProcessItemsInParallelFromIndex(state, step, items, startIndex, maxDegreeOfParallelism, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"ForEach failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ProcessItemsSequentiallyFromIndex(
        TState state,
        FlowStep step,
        List<object> items,
        int startIndex,
        CancellationToken cancellationToken)
    {
        // Process each item sequentially starting from the specified index
        for (int i = startIndex; i < items.Count; i++)
        {
            var item = items[i];
            var result = await ProcessSingleItemAsync(state, step, item, i, cancellationToken);
            if (!result.Success && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
            {
                return result;
            }
        }

        // Execute OnComplete callback if present
        if (step.CompiledStep != null)
        {
            step.CompiledStep.ExecuteOnComplete(state);
        }
        else if (step.OnComplete != null)
        {
            try
            {
                step.OnComplete.DynamicInvoke(state);
            }
            catch
            {
                // Ignore callback errors
            }
        }

        return StepResult.Succeeded();
    }

    private async Task<StepResult> ProcessItemsInParallelFromIndex(
        TState state,
        FlowStep step,
        List<object> items,
        int startIndex,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        // Process remaining items in parallel
        var remainingItems = items.Skip(startIndex).ToList();
        using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);
        var tasks = new List<Task<StepResult>>();

        try
        {
            foreach (var (item, relativeIndex) in remainingItems.Select((item, index) => (item, index)))
            {
                var actualIndex = startIndex + relativeIndex;
                tasks.Add(ProcessItemWithSemaphore(state, step, item, actualIndex, semaphore, cancellationToken));
            }

            var results = await Task.WhenAll(tasks);

            // Check for failures
            var firstFailure = results.FirstOrDefault(r => !r.Success);
            if (firstFailure.Success == false && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
            {
                return firstFailure;
            }

            // Execute OnComplete callback if present
            if (step.OnComplete != null)
            {
                try
                {
                    step.OnComplete.DynamicInvoke(state);
                }
                catch
                {
                    // Ignore callback errors
                }
            }

            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"Parallel processing failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ProcessItemsStreaming(
        TState state,
        FlowStep step,
        System.Collections.IEnumerable enumerable,
        CancellationToken cancellationToken)
    {
        try
        {
            var batchSize = step.BatchSize;
            var index = 0;
            var batch = new List<object>(batchSize);

            // Process items in small batches to control memory usage
            foreach (var item in enumerable)
            {
                batch.Add(item);

                // Process batch when it's full
                if (batch.Count >= batchSize)
                {
                    var result = await ProcessBatch(state, step, batch, index - batch.Count + 1, cancellationToken);
                    if (!result.Success && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
                    {
                        return result;
                    }

                    // Clear batch to free memory
                    batch.Clear();

                    // Force garbage collection periodically for large streams
                    if (index % (batchSize * 10) == 0)
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }
                }

                index++;
            }

            // Process remaining items in the final batch
            if (batch.Count > 0)
            {
                var result = await ProcessBatch(state, step, batch, index - batch.Count, cancellationToken);
                if (!result.Success && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
                {
                    return result;
                }
            }

            // Execute OnComplete callback if present
            if (step.OnComplete != null)
            {
                try
                {
                    step.OnComplete.DynamicInvoke(state);
                }
                catch
                {
                    // Ignore callback errors
                }
            }

            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"Streaming ForEach failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ProcessBatch(
        TState state,
        FlowStep step,
        List<object> batch,
        int startIndex,
        CancellationToken cancellationToken)
    {
        // Process each item in the batch sequentially
        for (int i = 0; i < batch.Count; i++)
        {
            var item = batch[i];
            var itemIndex = startIndex + i;
            var result = await ProcessSingleItemAsync(state, step, item, itemIndex, cancellationToken);

            if (!result.Success && step.FailureHandling == ForEachFailureHandling.StopOnFirstFailure)
            {
                return result;
            }
        }

        return StepResult.Succeeded();
    }

    private async Task<StepResult> ProcessItemWithSemaphore(
        TState state,
        FlowStep step,
        object item,
        int index,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await ProcessSingleItemAsync(state, step, item, index, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
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
