using System.Diagnostics;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Partial class for DslFlowExecutor containing loop and try-catch execution methods.
/// </summary>
public partial class DslFlowExecutor<TState, TConfig>
    where TState : class, IFlowState, new()
    where TConfig : FlowConfig<TState>
{
    private async Task<StepResult> ExecuteWhileAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.LoopCondition == null || step.LoopSteps == null)
            return StepResult.Failed("While step has no condition or loop steps");

        var condition = (Func<TState, bool>)step.LoopCondition;
        var sw = Stopwatch.StartNew();
        var iterationCount = 0;
        const int maxIterations = 10000;
        var loopTimeout = TimeSpan.FromMinutes(5);

        // Check for recovery from previous execution
        var loopProgress = await _store.GetLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
        if (loopProgress != null)
        {
            iterationCount = loopProgress.IterationCount;
            sw.Restart();
        }

        try
        {
            while (condition(state))
            {
                if (iterationCount >= maxIterations)
                {
                    await _store.ClearLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
                    return StepResult.Failed($"Loop iteration limit exceeded: {maxIterations}");
                }

                if (sw.Elapsed > loopTimeout)
                {
                    await _store.ClearLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
                    return StepResult.Failed($"Loop execution timeout exceeded: {loopTimeout.TotalSeconds}s");
                }

                var result = await ExecuteLoopStepsAsync(state, step.LoopSteps, cancellationToken);
                if (!result.Success && !result.Skipped)
                {
                    // Save progress for recovery
                    var progress = new LoopProgress
                    {
                        FlowId = state.FlowId,
                        StepIndex = stepIndex,
                        IterationCount = iterationCount,
                        StartedAt = DateTime.UtcNow.AddSeconds(-sw.Elapsed.TotalSeconds),
                        LastIterationAt = DateTime.UtcNow,
                        LastError = result.Error
                    };
                    await _store.SaveLoopProgressAsync(state.FlowId, stepIndex, progress, cancellationToken);
                    return result;
                }

                iterationCount++;
            }

            // Clear progress on successful completion
            await _store.ClearLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"While loop execution failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ExecuteDoWhileAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.LoopCondition == null || step.LoopSteps == null)
            return StepResult.Failed("DoWhile step has no condition or loop steps");

        var condition = (Func<TState, bool>)step.LoopCondition;
        var sw = Stopwatch.StartNew();
        var iterationCount = 0;
        const int maxIterations = 10000;
        var loopTimeout = TimeSpan.FromMinutes(5);

        // Check for recovery from previous execution
        var loopProgress = await _store.GetLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
        if (loopProgress != null)
        {
            iterationCount = loopProgress.IterationCount;
            sw.Restart();
        }

        try
        {
            do
            {
                if (iterationCount >= maxIterations)
                {
                    await _store.ClearLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
                    return StepResult.Failed($"Loop iteration limit exceeded: {maxIterations}");
                }

                if (sw.Elapsed > loopTimeout)
                {
                    await _store.ClearLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
                    return StepResult.Failed($"Loop execution timeout exceeded: {loopTimeout.TotalSeconds}s");
                }

                var result = await ExecuteLoopStepsAsync(state, step.LoopSteps, cancellationToken);
                if (!result.Success && !result.Skipped)
                {
                    // Save progress for recovery
                    var progress = new LoopProgress
                    {
                        FlowId = state.FlowId,
                        StepIndex = stepIndex,
                        IterationCount = iterationCount,
                        StartedAt = DateTime.UtcNow.AddSeconds(-sw.Elapsed.TotalSeconds),
                        LastIterationAt = DateTime.UtcNow,
                        LastError = result.Error
                    };
                    await _store.SaveLoopProgressAsync(state.FlowId, stepIndex, progress, cancellationToken);
                    return result;
                }

                iterationCount++;
            } while (condition(state));

            // Clear progress on successful completion
            await _store.ClearLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"Do-While loop execution failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ExecuteRepeatAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.LoopSteps == null)
            return StepResult.Failed("Repeat step has no loop steps");

        int times = 0;
        if (step.RepeatCount.HasValue)
        {
            times = step.RepeatCount.Value;
        }
        else if (step.RepeatCountSelector != null)
        {
            try
            {
                var timesSelector = (Func<TState, int>)step.RepeatCountSelector;
                times = timesSelector(state);
            }
            catch (Exception ex)
            {
                return StepResult.Failed($"Repeat count selector failed: {ex.Message}");
            }
        }
        else
        {
            return StepResult.Failed("Repeat step has no count or count selector");
        }

        const int maxIterations = 10000;
        if (times > maxIterations)
            return StepResult.Failed($"Repeat count exceeds iteration limit: {times} > {maxIterations}");

        var sw = Stopwatch.StartNew();
        var loopTimeout = TimeSpan.FromMinutes(5);
        var startIndex = 0;

        // Check for recovery from previous execution
        var loopProgress = await _store.GetLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
        if (loopProgress != null)
        {
            startIndex = loopProgress.IterationCount;
            sw.Restart();
        }

        try
        {
            for (int i = startIndex; i < times; i++)
            {
                if (sw.Elapsed > loopTimeout)
                {
                    await _store.ClearLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
                    return StepResult.Failed($"Loop execution timeout exceeded: {loopTimeout.TotalSeconds}s");
                }

                var result = await ExecuteLoopStepsAsync(state, step.LoopSteps, cancellationToken);
                if (!result.Success && !result.Skipped)
                {
                    // Save progress for recovery
                    var progress = new LoopProgress
                    {
                        FlowId = state.FlowId,
                        StepIndex = stepIndex,
                        IterationCount = i,
                        StartedAt = DateTime.UtcNow.AddSeconds(-sw.Elapsed.TotalSeconds),
                        LastIterationAt = DateTime.UtcNow,
                        LastError = result.Error
                    };
                    await _store.SaveLoopProgressAsync(state.FlowId, stepIndex, progress, cancellationToken);
                    return result;
                }
            }

            // Clear progress on successful completion
            await _store.ClearLoopProgressAsync(state.FlowId, stepIndex, cancellationToken);
            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"Repeat loop execution failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ExecuteTryAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.TrySteps == null)
            return StepResult.Failed("Try step has no try steps");

        try
        {
            var result = await ExecuteLoopStepsAsync(state, step.TrySteps, cancellationToken);
            if (!result.Success && !result.Skipped)
                return result;

            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            // Try to find matching catch handler
            if (step.CatchHandlers != null)
            {
                foreach (var (exceptionType, handler) in step.CatchHandlers)
                {
                    if (exceptionType.IsInstanceOfType(ex))
                    {
                        try
                        {
                            var action = (Action<TState, Exception>)handler;
                            action(state, ex);
                            return StepResult.Succeeded();
                        }
                        catch (Exception handlerEx)
                        {
                            return StepResult.Failed($"Catch handler failed: {handlerEx.Message}");
                        }
                    }
                }
            }

            return StepResult.Failed($"Unhandled exception: {ex.Message}");
        }
        finally
        {
            // Execute finally block
            if (step.FinallyHandler != null)
            {
                try
                {
                    var finallyAction = (Action<TState>)step.FinallyHandler;
                    finallyAction(state);
                }
                catch
                {
                    // Ignore finally block errors
                }
            }
        }
    }

    private async Task<StepResult> ExecuteCallFlowAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.RequestFactory == null || step.Metadata == null)
            return StepResult.Failed("CallFlow step has no factory or flow type");

        try
        {
            // Create the nested flow state using the factory
            var nestedState = step.RequestFactory.DynamicInvoke(state) as IFlowState;
            if (nestedState == null)
                return StepResult.Failed("CallFlow factory did not produce a valid IFlowState");

            // For now, we return success as the actual recursive flow execution
            // would require access to the service provider to resolve the flow executor
            // This is a placeholder for future implementation
            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"CallFlow execution failed: {ex.Message}");
        }
    }

    private async Task<StepResult> ExecuteLoopStepsAsync(
        TState state,
        List<FlowStep> steps,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var result = await ExecuteStepAsync(state, step, i, cancellationToken);

            if (result.IsSuspended)
                return result;

            if (!result.Success && !result.Skipped)
                return result;

            // Apply result to state if step has result setter
            if (result.Success && result.Result != null && step.ResultSetter != null)
            {
                try
                {
                    step.ResultSetter.DynamicInvoke(state, result.Result);
                }
                catch
                {
                    // Ignore setter errors
                }
            }
        }

        return StepResult.Succeeded();
    }
}
