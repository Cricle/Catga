namespace Catga.Flow.Dsl;

public partial class DslFlowExecutor<TState, TConfig>
    where TState : class, IFlowState, new()
    where TConfig : FlowConfig<TState>
{
    private async Task<StepResult> ExecuteParallelAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        CancellationToken cancellationToken)
    {
        if (step.ParallelBranches == null || step.ParallelBranches.Count == 0)
            return StepResult.Succeeded();

        var flowId = state.FlowId ?? throw new InvalidOperationException("FlowId is required for Parallel execution");

        var suspendingBranches = step.ParallelBranches
            .Select((branch, index) => new { Index = index, Step = FindFirstSuspendingStep(branch) })
            .Where(branch => branch.Step != null)
            .ToList();

        if (!step.ParallelWaitAll)
        {
            var unsupportedStep = suspendingBranches.FirstOrDefault()?.Step;
            if (unsupportedStep != null)
                return StepResult.Failed(BuildUnsupportedSuspendingStepMessage("Parallel.WaitAny", unsupportedStep.Type));
        }

        using var cts = step.Timeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (cts != null && step.Timeout.HasValue)
            cts.CancelAfter(step.Timeout.Value);

        var ct = cts?.Token ?? cancellationToken;

        var branchTasks = step.ParallelBranches
            .Select((branch, index) => ExecuteParallelBranchAsync(state, stepIndex, index, branch, ct))
            .ToList();

        if (step.ParallelWaitAll)
        {
            var results = await Task.WhenAll(branchTasks);
            var progress = CreateParallelProgress(step.ParallelBranches.Count, results);
            var hasSuspended = results.Any(r => r.Result.IsSuspended);

            if (hasSuspended)
            {
                await _store.SaveParallelProgressAsync(flowId, stepIndex, progress, cancellationToken);
                return StepResult.Suspended(position: StepToPosition(stepIndex));
            }

            await _store.ClearParallelProgressAsync(flowId, stepIndex, cancellationToken);

            var failed = results
                .Select(r => r.Result)
                .FirstOrDefault(r => !r.Success && !r.Skipped);
            return failed.Error != null
                ? StepResult.Failed(failed.Error, StepToPosition(stepIndex))
                : StepResult.Succeeded(position: StepToPosition(stepIndex));
        }
        else
        {
            var completed = await Task.WhenAny(branchTasks);
            var result = await completed;
            return result.Result;
        }
    }

    private async Task<StepResult> ResumeParallelAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        ParallelProgress progress,
        CancellationToken cancellationToken)
    {
        if (step.ParallelBranches == null || step.ParallelBranches.Count == 0)
            return StepResult.Succeeded(position: StepToPosition(stepIndex));

        var flowId = state.FlowId ?? throw new InvalidOperationException("FlowId is required for Parallel recovery");

        var branchTasks = progress.Branches
            .Select(branch => ResumeParallelBranchAsync(state, step, stepIndex, branch, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(branchTasks);
        var updatedProgress = CreateParallelProgress(step.ParallelBranches.Count, results);
        var hasSuspended = results.Any(r => r.Result.IsSuspended);

        if (hasSuspended)
        {
            await _store.SaveParallelProgressAsync(flowId, stepIndex, updatedProgress, cancellationToken);
            return StepResult.Suspended(position: StepToPosition(stepIndex));
        }

        await _store.ClearParallelProgressAsync(flowId, stepIndex, cancellationToken);

        var failedBranch = updatedProgress.Branches.FirstOrDefault(branch => branch.Status == ParallelBranchStatus.Failed);
        if (failedBranch != null)
            return StepResult.Failed(failedBranch.Error ?? "Parallel branch failed", StepToPosition(stepIndex));

        return StepResult.Succeeded(position: StepToPosition(stepIndex));
    }

    private async Task<StepResult> ExecuteThrottleAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        CancellationToken cancellationToken)
    {
        if (step.ThrottleSteps == null || step.ThrottleSteps.Count == 0)
            return StepResult.Succeeded();

        using var semaphore = new SemaphoreSlim(step.ThrottleCount, step.ThrottleCount);
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteBranchStepsFromAsync(state, step.ThrottleSteps, position, 0, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>Execute a list of steps sequentially as a parallel branch.</summary>
    private async Task<ParallelBranchExecutionResult> ExecuteParallelBranchAsync(
        TState state,
        int stepIndex,
        int branchIndex,
        List<FlowStep> steps,
        CancellationToken cancellationToken)
    {
        var branchPosition = StepToPosition(stepIndex).EnterBranch(branchIndex);
        var result = await ExecuteBranchStepsFromAsync(state, steps, branchPosition, 0, cancellationToken);
        return new ParallelBranchExecutionResult(branchIndex, result);
    }

    private async Task<ParallelBranchExecutionResult> ResumeParallelBranchAsync(
        TState state,
        FlowStep step,
        int stepIndex,
        ParallelBranchProgress branch,
        CancellationToken cancellationToken)
    {
        if (branch.Status is ParallelBranchStatus.Completed or ParallelBranchStatus.Failed)
        {
            var preserved = branch.Status == ParallelBranchStatus.Failed
                ? StepResult.Failed(branch.Error ?? "Parallel branch failed")
                : StepResult.Succeeded();
            return new ParallelBranchExecutionResult(branch.BranchIndex, preserved);
        }

        if (step.ParallelBranches == null || branch.BranchIndex < 0 || branch.BranchIndex >= step.ParallelBranches.Count)
        {
            return new ParallelBranchExecutionResult(
                branch.BranchIndex,
                StepResult.Failed($"Invalid parallel branch index {branch.BranchIndex} at step {stepIndex}"));
        }

        var branchSteps = step.ParallelBranches[branch.BranchIndex];
        StepResult result;

        if (branch.Position != null)
        {
            var waitCondition = await _store.GetWaitConditionAsync(
                BuildWaitConditionCorrelationId(state.FlowId!, branch.Position),
                cancellationToken);

            if (waitCondition != null)
            {
                var suspendedStep = GetStepAtPosition(branch.Position);
                if (suspendedStep == null)
                {
                    return new ParallelBranchExecutionResult(
                        branch.BranchIndex,
                        StepResult.Failed($"Unable to resolve suspended parallel branch step at {string.Join(",", branch.Position.Path)}"));
                }

                var waitResult = await EvaluateWaitConditionAsync(
                    state,
                    suspendedStep,
                    branch.Position,
                    waitCondition,
                    cancellationToken);

                if (waitResult.IsSuspended || !waitResult.Success)
                    return new ParallelBranchExecutionResult(branch.BranchIndex, waitResult);
            }

            result = await ResumeScopedStepsAsync(state, branchSteps, branch.Position, 1, resumeCurrentStep: false, cancellationToken);
        }
        else
        {
            var branchPosition = StepToPosition(stepIndex).EnterBranch(branch.BranchIndex);
            result = await ExecuteBranchStepsFromAsync(state, branchSteps, branchPosition, 0, cancellationToken);
        }

        return new ParallelBranchExecutionResult(branch.BranchIndex, result);
    }

    private static ParallelProgress CreateParallelProgress(
        int branchCount,
        IEnumerable<ParallelBranchExecutionResult> results)
    {
        return new ParallelProgress
        {
            BranchCount = branchCount,
            Branches = results
                .OrderBy(result => result.BranchIndex)
                .Select(result => new ParallelBranchProgress
                {
                    BranchIndex = result.BranchIndex,
                    Status = result.Result.IsSuspended
                        ? ParallelBranchStatus.Suspended
                        : result.Result.Success
                            ? ParallelBranchStatus.Completed
                            : ParallelBranchStatus.Failed,
                    Position = result.Result.IsSuspended ? result.Result.Position : null,
                    Error = result.Result.Success ? null : result.Result.Error
                })
                .ToList()
        };
    }

    private readonly record struct ParallelBranchExecutionResult(int BranchIndex, StepResult Result);
}
