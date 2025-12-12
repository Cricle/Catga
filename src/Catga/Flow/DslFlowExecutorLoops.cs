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
        var loopExecutor = new LoopExecutor<TState>(
            async (s, steps, _, ct) => await ExecuteLoopStepsAsync(s, steps, ct),
            maxLoopDepth: 1000,
            maxIterations: 10000,
            loopTimeout: TimeSpan.FromMinutes(5));

        return await loopExecutor.ExecuteWhileAsync(state, condition, step.LoopSteps, cancellationToken);
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
        var loopExecutor = new LoopExecutor<TState>(
            async (s, steps, _, ct) => await ExecuteLoopStepsAsync(s, steps, ct),
            maxLoopDepth: 1000,
            maxIterations: 10000,
            loopTimeout: TimeSpan.FromMinutes(5));

        return await loopExecutor.ExecuteDoWhileAsync(state, condition, step.LoopSteps, cancellationToken);
    }

    private async Task<StepResult> ExecuteRepeatAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.LoopSteps == null)
            return StepResult.Failed("Repeat step has no loop steps");

        var loopExecutor = new LoopExecutor<TState>(
            async (s, steps, _, ct) => await ExecuteLoopStepsAsync(s, steps, ct),
            maxLoopDepth: 1000,
            maxIterations: 10000,
            loopTimeout: TimeSpan.FromMinutes(5));

        if (step.RepeatCount.HasValue)
        {
            return await loopExecutor.ExecuteRepeatAsync(state, step.RepeatCount.Value, step.LoopSteps, cancellationToken);
        }
        else if (step.RepeatCountSelector != null)
        {
            var timesSelector = (Func<TState, int>)step.RepeatCountSelector;
            return await loopExecutor.ExecuteRepeatAsync(state, timesSelector, step.LoopSteps, cancellationToken);
        }

        return StepResult.Failed("Repeat step has no count or count selector");
    }

    private async Task<StepResult> ExecuteTryAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.TrySteps == null)
            return StepResult.Failed("Try step has no try steps");

        var tryCatchExecutor = new TryCatchExecutor<TState>(
            async (s, steps, _, ct) => await ExecuteLoopStepsAsync(s, steps, ct));

        // Convert catch handlers from (Type, Delegate) to (Type, Action<TState, Exception>)
        List<(Type, Action<TState, Exception>)>? catchHandlers = null;
        if (step.CatchHandlers != null && step.CatchHandlers.Count > 0)
        {
            catchHandlers = new List<(Type, Action<TState, Exception>)>();
            foreach (var (exceptionType, handler) in step.CatchHandlers)
            {
                var action = (Action<TState, Exception>)handler;
                catchHandlers.Add((exceptionType, action));
            }
        }

        // Get finally handler if present
        Action<TState>? finallyHandler = null;
        if (step.FinallyHandler != null)
        {
            finallyHandler = (Action<TState>)step.FinallyHandler;
        }

        return await tryCatchExecutor.ExecuteTryCatchAsync(
            state,
            step.TrySteps,
            catchHandlers,
            finallyHandler,
            cancellationToken);
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
