using Catga.Abstractions;

namespace Catga.Flow.Dsl;

public partial class DslFlowExecutor<TState, TConfig>
    where TState : class, IFlowState, new()
    where TConfig : FlowConfig<TState>
{
    private async Task<StepResult> ExecuteIfAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        CancellationToken cancellationToken)
    {
        if (step.BranchCondition == null)
            return StepResult.Failed("If step has no condition", position);

        var condition = (Func<TState, bool>)step.BranchCondition;
        var conditionResult = condition(state);
        List<FlowStep>? branchToExecute = null;
        int branchIndex = 0;

        if (conditionResult)
        {
            branchToExecute = step.ThenBranch;
            branchIndex = 0;
        }
        else if (step.ElseIfBranches != null)
        {
            int elseIfIndex = 1;
            foreach (var (elseIfCondition, elseIfBranch) in step.ElseIfBranches)
            {
                var elseIfFunc = (Func<TState, bool>)elseIfCondition;
                if (elseIfFunc(state))
                {
                    branchToExecute = elseIfBranch;
                    branchIndex = elseIfIndex;
                    break;
                }
                elseIfIndex++;
            }
        }

        if (branchToExecute == null && step.ElseBranch != null)
        {
            branchToExecute = step.ElseBranch;
            branchIndex = -1;
        }

        if (branchToExecute != null && branchToExecute.Count > 0)
        {
            var branchPosition = position.EnterBranch(branchIndex);
            var result = await ExecuteBranchStepsFromAsync(state, branchToExecute, branchPosition, 0, cancellationToken);
            if (result.IsSuspended || (!result.Success && !result.Skipped))
                return result;
        }

        return StepResult.Succeeded(position: position);
    }

    private async Task<StepResult> ExecuteSwitchAsync(
        TState state,
        FlowStep step,
        FlowPosition position,
        CancellationToken cancellationToken)
    {
        if (step.EvaluateSwitchSelector == null)
            return StepResult.Failed("Switch step has no selector", position);

        var selectorValue = step.EvaluateSwitchSelector(state);
        List<FlowStep>? branchToExecute = null;
        int caseIndex = -1;

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

        if (branchToExecute == null && step.DefaultBranch != null)
        {
            branchToExecute = step.DefaultBranch;
            caseIndex = -1;
        }

        if (branchToExecute != null && branchToExecute.Count > 0)
        {
            var branchPosition = position.EnterBranch(caseIndex);
            var result = await ExecuteBranchStepsFromAsync(state, branchToExecute, branchPosition, 0, cancellationToken);
            if (result.IsSuspended || (!result.Success && !result.Skipped))
                return result;
        }

        return StepResult.Succeeded(position: position);
    }

    private async Task<StepResult> ExecuteBranchStepsAsync(
        TState state,
        List<FlowStep> steps,
        FlowPosition parentPosition,
        CancellationToken cancellationToken)
        => await ExecuteBranchStepsFromAsync(state, steps, parentPosition, 0, cancellationToken);

    private async Task<StepResult> ExecuteBranchStepsFromAsync(
        TState state,
        List<FlowStep> steps,
        FlowPosition parentPosition,
        int startIndex,
        CancellationToken cancellationToken)
    {
        for (int i = startIndex; i < steps.Count; i++)
        {
            var branchStep = steps[i];
            var nestedPosition = parentPosition.EnterBranch(i);
            var result = await ExecuteStepAsync(state, branchStep, nestedPosition, cancellationToken);

            if (result.IsSuspended)
                return result;

            if (!result.Success && !result.Skipped)
                return result;

            if (result.Success && result.Result != null && branchStep.SetResult != null)
            {
                branchStep.SetResult(state, result.Result);
            }
        }

        return StepResult.Succeeded(position: parentPosition);
    }

    private List<FlowStep>? GetBranchAtPosition(FlowStep step, int branchIndex)
    {
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
}
