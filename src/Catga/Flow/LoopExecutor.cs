using System.Diagnostics;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Executor for While/DoWhile/Repeat loops with safety mechanisms.
/// </summary>
internal class LoopExecutor<TState> where TState : class, IFlowState
{
    private readonly Func<TState, List<FlowStep>, int, CancellationToken, Task<StepResult>> _executeStepsAsync;

    // Safety limits (configurable)
    private readonly int _maxLoopDepth;
    private readonly int _maxIterations;
    private readonly TimeSpan _loopTimeout;

    public LoopExecutor(
        Func<TState, List<FlowStep>, int, CancellationToken, Task<StepResult>> executeStepsAsync,
        int maxLoopDepth = 1000,
        int maxIterations = 10000,
        TimeSpan? loopTimeout = null)
    {
        _executeStepsAsync = executeStepsAsync;
        _maxLoopDepth = maxLoopDepth;
        _maxIterations = maxIterations;
        _loopTimeout = loopTimeout ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Execute a While loop.
    /// </summary>
    public async Task<StepResult> ExecuteWhileAsync(
        TState state,
        Func<TState, bool> condition,
        List<FlowStep> loopSteps,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var iterationCount = 0;

        try
        {
            while (condition(state))
            {
                // Check iteration limit
                if (iterationCount >= _maxIterations)
                {
                    return StepResult.Failed($"Loop iteration limit exceeded: {_maxIterations}");
                }

                // Check timeout
                if (sw.Elapsed > _loopTimeout)
                {
                    return StepResult.Failed($"Loop execution timeout exceeded: {_loopTimeout.TotalSeconds}s");
                }

                // Execute loop body
                var result = await _executeStepsAsync(state, loopSteps, 0, cancellationToken);
                if (!result.Success && !result.Skipped)
                {
                    return result;
                }

                iterationCount++;
            }

            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"While loop execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute a Do-While loop.
    /// </summary>
    public async Task<StepResult> ExecuteDoWhileAsync(
        TState state,
        Func<TState, bool> condition,
        List<FlowStep> loopSteps,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var iterationCount = 0;

        try
        {
            do
            {
                // Check iteration limit
                if (iterationCount >= _maxIterations)
                {
                    return StepResult.Failed($"Loop iteration limit exceeded: {_maxIterations}");
                }

                // Check timeout
                if (sw.Elapsed > _loopTimeout)
                {
                    return StepResult.Failed($"Loop execution timeout exceeded: {_loopTimeout.TotalSeconds}s");
                }

                // Execute loop body
                var result = await _executeStepsAsync(state, loopSteps, 0, cancellationToken);
                if (!result.Success && !result.Skipped)
                {
                    return result;
                }

                iterationCount++;
            } while (condition(state));

            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"Do-While loop execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute a Repeat loop with fixed iteration count.
    /// </summary>
    public async Task<StepResult> ExecuteRepeatAsync(
        TState state,
        int times,
        List<FlowStep> loopSteps,
        CancellationToken cancellationToken)
    {
        // Check iteration limit
        if (times > _maxIterations)
        {
            return StepResult.Failed($"Repeat count exceeds iteration limit: {times} > {_maxIterations}");
        }

        var sw = Stopwatch.StartNew();

        try
        {
            for (int i = 0; i < times; i++)
            {
                // Check timeout
                if (sw.Elapsed > _loopTimeout)
                {
                    return StepResult.Failed($"Loop execution timeout exceeded: {_loopTimeout.TotalSeconds}s");
                }

                // Execute loop body
                var result = await _executeStepsAsync(state, loopSteps, 0, cancellationToken);
                if (!result.Success && !result.Skipped)
                {
                    return result;
                }
            }

            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"Repeat loop execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute a Repeat loop with dynamic iteration count.
    /// </summary>
    public async Task<StepResult> ExecuteRepeatAsync(
        TState state,
        Func<TState, int> timesSelector,
        List<FlowStep> loopSteps,
        CancellationToken cancellationToken)
    {
        try
        {
            var times = timesSelector(state);
            return await ExecuteRepeatAsync(state, times, loopSteps, cancellationToken);
        }
        catch (Exception ex)
        {
            return StepResult.Failed($"Repeat loop execution failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Try-Catch-Finally executor with safety mechanisms.
/// </summary>
internal class TryCatchExecutor<TState> where TState : class, IFlowState
{
    private readonly Func<TState, List<FlowStep>, int, CancellationToken, Task<StepResult>> _executeStepsAsync;

    public TryCatchExecutor(
        Func<TState, List<FlowStep>, int, CancellationToken, Task<StepResult>> executeStepsAsync)
    {
        _executeStepsAsync = executeStepsAsync;
    }

    /// <summary>
    /// Execute a Try-Catch-Finally block.
    /// </summary>
    public async Task<StepResult> ExecuteTryCatchAsync(
        TState state,
        List<FlowStep> trySteps,
        List<(Type ExceptionType, Action<TState, Exception> Handler)>? catchHandlers,
        Action<TState>? finallyHandler,
        CancellationToken cancellationToken)
    {
        try
        {
            // Execute try block
            var result = await _executeStepsAsync(state, trySteps, 0, cancellationToken);
            if (!result.Success && !result.Skipped)
            {
                return result;
            }

            return StepResult.Succeeded();
        }
        catch (Exception ex)
        {
            // Try to find matching catch handler
            if (catchHandlers != null)
            {
                foreach (var (exceptionType, handler) in catchHandlers)
                {
                    if (exceptionType.IsInstanceOfType(ex))
                    {
                        try
                        {
                            handler(state, ex);
                            return StepResult.Succeeded();
                        }
                        catch (Exception handlerEx)
                        {
                            return StepResult.Failed($"Catch handler failed: {handlerEx.Message}");
                        }
                    }
                }
            }

            // No matching catch handler
            return StepResult.Failed($"Unhandled exception: {ex.Message}");
        }
        finally
        {
            // Execute finally block
            if (finallyHandler != null)
            {
                try
                {
                    finallyHandler(state);
                }
                catch
                {
                    // Ignore finally block errors
                }
            }
        }
    }
}
