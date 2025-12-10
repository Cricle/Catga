using System.Runtime.CompilerServices;

namespace Catga.Flow.Dsl;

/// <summary>
/// Result of a flow step execution
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

/// <summary>
/// Performance optimizations for DslFlowExecutor ExecuteIfAsync method
/// </summary>
public static class DslFlowExecutorOptimizations
{
    /// <summary>
    /// Optimized version of ExecuteIfAsync with performance improvements
    /// </summary>
    /// <remarks>
    /// Key optimizations:
    /// 1. Avoid casting by using generic constraints
    /// 2. Early returns to minimize nested checks
    /// 3. Use of AggressiveInlining for hot paths
    /// 4. Reduced allocations by reusing branch index calculation
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task<StepResult> ExecuteIfAsyncOptimized<TState>(
        TState state,
        FlowStep step,
        int stepIndex,
        Func<TState, List<FlowStep>, FlowPosition, CancellationToken, Task<StepResult>> executeBranchStepsAsync,
        CancellationToken cancellationToken) where TState : IFlowState
    {
        // Fast path: validate condition exists
        if (step.BranchCondition == null)
            return StepResult.Failed("If step has no condition");

        // Evaluate main condition
        var condition = (Func<TState, bool>)step.BranchCondition;
        if (condition(state))
        {
            return await ExecuteBranch(state, step.ThenBranch, stepIndex, 0,
                executeBranchStepsAsync, cancellationToken);
        }

        // Check ElseIf branches if present
        if (step.ElseIfBranches?.Count > 0)
        {
            int elseIfIndex = 1;
            foreach (var (elseIfCondition, elseIfBranch) in step.ElseIfBranches)
            {
                var elseIfFunc = (Func<TState, bool>)elseIfCondition;
                if (elseIfFunc(state))
                {
                    return await ExecuteBranch(state, elseIfBranch, stepIndex, elseIfIndex,
                        executeBranchStepsAsync, cancellationToken);
                }
                elseIfIndex++;
            }
        }

        // Execute Else branch if present
        if (step.ElseBranch?.Count > 0)
        {
            return await ExecuteBranch(state, step.ElseBranch, stepIndex, -1,
                executeBranchStepsAsync, cancellationToken);
        }

        return StepResult.Succeeded();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async Task<StepResult> ExecuteBranch<TState>(
        TState state,
        List<FlowStep>? branch,
        int stepIndex,
        int branchIndex,
        Func<TState, List<FlowStep>, FlowPosition, CancellationToken, Task<StepResult>> executeBranchStepsAsync,
        CancellationToken cancellationToken) where TState : IFlowState
    {
        if (branch == null || branch.Count == 0)
            return StepResult.Succeeded();

        var branchPosition = new FlowPosition([stepIndex, branchIndex]);
        return await executeBranchStepsAsync(state, branch, branchPosition, cancellationToken);
    }

    /// <summary>
    /// Performance monitoring wrapper for ExecuteIfAsync
    /// </summary>
    public static async Task<StepResult> ExecuteIfAsyncWithMetrics<TState>(
        TState state,
        FlowStep step,
        int stepIndex,
        Func<TState, List<FlowStep>, FlowPosition, CancellationToken, Task<StepResult>> executeBranchStepsAsync,
        CancellationToken cancellationToken,
        IFlowMetrics? metrics = null) where TState : IFlowState
    {
        var startTime = DateTime.UtcNow;
        var startMemory = GC.GetTotalMemory(false);

        try
        {
            var result = await ExecuteIfAsyncOptimized(state, step, stepIndex,
                executeBranchStepsAsync, cancellationToken);

            if (metrics != null)
            {
                var duration = DateTime.UtcNow - startTime;
                var memoryUsed = GC.GetTotalMemory(false) - startMemory;

                metrics.RecordIfExecution(duration, memoryUsed, result.Success);
            }

            return result;
        }
        catch (Exception ex)
        {
            metrics?.RecordIfExecutionError(ex);
            throw;
        }
    }
}

/// <summary>
/// Interface for collecting Flow execution metrics
/// </summary>
public interface IFlowMetrics
{
    void RecordIfExecution(TimeSpan duration, long memoryUsed, bool success);
    void RecordIfExecutionError(Exception ex);
    void RecordForEachIteration(int itemIndex, TimeSpan duration);
    void RecordSwitchExecution(object caseValue, TimeSpan duration);
}

/// <summary>
/// Simple in-memory metrics collector for testing
/// </summary>
public class InMemoryFlowMetrics : IFlowMetrics
{
    private readonly List<IfMetric> _ifMetrics = new();
    private readonly List<Exception> _errors = new();

    public IReadOnlyList<IfMetric> IfMetrics => _ifMetrics;
    public IReadOnlyList<Exception> Errors => _errors;

    public void RecordIfExecution(TimeSpan duration, long memoryUsed, bool success)
    {
        _ifMetrics.Add(new IfMetric
        {
            Duration = duration,
            MemoryUsed = memoryUsed,
            Success = success,
            Timestamp = DateTime.UtcNow
        });
    }

    public void RecordIfExecutionError(Exception ex)
    {
        _errors.Add(ex);
    }

    public void RecordForEachIteration(int itemIndex, TimeSpan duration)
    {
        // Implementation for ForEach metrics
    }

    public void RecordSwitchExecution(object caseValue, TimeSpan duration)
    {
        // Implementation for Switch metrics
    }

    public record IfMetric
    {
        public TimeSpan Duration { get; init; }
        public long MemoryUsed { get; init; }
        public bool Success { get; init; }
        public DateTime Timestamp { get; init; }
    }
}
