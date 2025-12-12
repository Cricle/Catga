using System.Collections.Generic;
using System.Linq;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Static analyzer for Flow DSL to validate structure and safety.
/// </summary>
public class FlowAnalyzer<TState> where TState : class, IFlowState
{
    private readonly FlowConfig<TState> _config;
    private readonly List<FlowAnalysisResult> _results = new();

    public FlowAnalyzer(FlowConfig<TState> config)
    {
        _config = config;
    }

    /// <summary>
    /// Analyze the flow for potential issues.
    /// </summary>
    public FlowAnalysisReport Analyze()
    {
        _results.Clear();

        var stepsList = _config.Steps?.ToList();
        ValidateSteps(stepsList, 0);
        ValidateLoopSafety(stepsList);
        ValidateRecursionDepth(stepsList);
        ValidateExceptionHandling(stepsList);

        return new FlowAnalysisReport
        {
            IsValid = !_results.Any(r => r.Severity == AnalysisSeverity.Error),
            Results = _results.ToList()
        };
    }

    private void ValidateSteps(List<FlowStep>? steps, int depth)
    {
        if (steps == null || steps.Count == 0)
            return;

        foreach (var step in steps)
        {
            ValidateStep(step, depth);
        }
    }

    private void ValidateStep(FlowStep step, int depth)
    {
        if (step == null)
        {
            _results.Add(new FlowAnalysisResult
            {
                Severity = AnalysisSeverity.Error,
                Message = "Null step found in flow"
            });
            return;
        }

        switch (step.Type)
        {
            case StepType.If:
                if (step.BranchCondition == null)
                {
                    _results.Add(new FlowAnalysisResult
                    {
                        Severity = AnalysisSeverity.Error,
                        Message = "If step has no condition"
                    });
                }
                if (step.ThenBranch != null)
                    ValidateSteps(step.ThenBranch, depth + 1);
                if (step.ElseBranch != null)
                    ValidateSteps(step.ElseBranch, depth + 1);
                break;

            case StepType.While:
            case StepType.DoWhile:
                if (step.LoopCondition == null)
                {
                    _results.Add(new FlowAnalysisResult
                    {
                        Severity = AnalysisSeverity.Error,
                        Message = $"{step.Type} step has no condition"
                    });
                }
                if (step.LoopSteps != null)
                    ValidateSteps(step.LoopSteps, depth + 1);
                break;

            case StepType.Repeat:
                if (step.RepeatCount == null && step.RepeatCountSelector == null)
                {
                    _results.Add(new FlowAnalysisResult
                    {
                        Severity = AnalysisSeverity.Error,
                        Message = "Repeat step has no count or count selector"
                    });
                }
                if (step.LoopSteps != null)
                    ValidateSteps(step.LoopSteps, depth + 1);
                break;

            case StepType.Try:
                if (step.TrySteps == null || step.TrySteps.Count == 0)
                {
                    _results.Add(new FlowAnalysisResult
                    {
                        Severity = AnalysisSeverity.Warning,
                        Message = "Try step has no steps"
                    });
                }
                if (step.TrySteps != null)
                    ValidateSteps(step.TrySteps, depth + 1);
                break;

            case StepType.ForEach:
                if (step.CollectionSelector == null)
                {
                    _results.Add(new FlowAnalysisResult
                    {
                        Severity = AnalysisSeverity.Error,
                        Message = "ForEach step has no collection selector"
                    });
                }
                if (step.ItemSteps != null)
                    ValidateSteps(step.ItemSteps, depth + 1);
                break;

            case StepType.CallFlow:
                if (step.RequestFactory == null)
                {
                    _results.Add(new FlowAnalysisResult
                    {
                        Severity = AnalysisSeverity.Error,
                        Message = "CallFlow step has no state factory"
                    });
                }
                break;
        }
    }

    private void ValidateLoopSafety(List<FlowStep>? steps)
    {
        var loopCount = CountLoops(steps);
        if (loopCount > 10)
        {
            _results.Add(new FlowAnalysisResult
            {
                Severity = AnalysisSeverity.Warning,
                Message = $"Flow contains {loopCount} loops, which may impact performance"
            });
        }
    }

    private void ValidateRecursionDepth(List<FlowStep>? steps)
    {
        var maxDepth = CalculateMaxDepth(steps);
        if (maxDepth > 20)
        {
            _results.Add(new FlowAnalysisResult
            {
                Severity = AnalysisSeverity.Warning,
                Message = $"Flow has nesting depth of {maxDepth}, which may impact performance"
            });
        }
    }

    private void ValidateExceptionHandling(List<FlowStep>? steps)
    {
        var hasTry = HasTryBlock(steps);
        var hasRiskyOperations = HasRiskyOperations(steps);

        if (hasRiskyOperations && !hasTry)
        {
            _results.Add(new FlowAnalysisResult
            {
                Severity = AnalysisSeverity.Warning,
                Message = "Flow has potentially risky operations but no Try-Catch block"
            });
        }
    }

    private int CountLoops(List<FlowStep>? steps)
    {
        if (steps == null)
            return 0;

        var count = 0;
        foreach (var step in steps)
        {
            if (step.Type == StepType.While || step.Type == StepType.DoWhile ||
                step.Type == StepType.Repeat || step.Type == StepType.ForEach)
            {
                count++;
            }

            if (step.ThenBranch != null)
                count += CountLoops(step.ThenBranch);
            if (step.ElseBranch != null)
                count += CountLoops(step.ElseBranch);
            if (step.LoopSteps != null)
                count += CountLoops(step.LoopSteps);
            if (step.ItemSteps != null)
                count += CountLoops(step.ItemSteps);
            if (step.TrySteps != null)
                count += CountLoops(step.TrySteps);
        }

        return count;
    }

    private int CalculateMaxDepth(List<FlowStep>? steps, int currentDepth = 0)
    {
        if (steps == null || steps.Count == 0)
            return currentDepth;

        var maxDepth = currentDepth;
        foreach (var step in steps)
        {
            var depth = currentDepth + 1;

            if (step.ThenBranch != null)
                depth = Math.Max(depth, CalculateMaxDepth(step.ThenBranch.ToList(), currentDepth + 1));
            if (step.ElseBranch != null)
                depth = Math.Max(depth, CalculateMaxDepth(step.ElseBranch.ToList(), currentDepth + 1));
            if (step.LoopSteps != null)
                depth = Math.Max(depth, CalculateMaxDepth(step.LoopSteps.ToList(), currentDepth + 1));
            if (step.ItemSteps != null)
                depth = Math.Max(depth, CalculateMaxDepth(step.ItemSteps.ToList(), currentDepth + 1));
            if (step.TrySteps != null)
                depth = Math.Max(depth, CalculateMaxDepth(step.TrySteps.ToList(), currentDepth + 1));

            maxDepth = Math.Max(maxDepth, depth);
        }

        return maxDepth;
    }

    private bool HasTryBlock(List<FlowStep>? steps)
    {
        if (steps == null)
            return false;

        foreach (var step in steps)
        {
            if (step.Type == StepType.Try)
                return true;

            if (step.ThenBranch != null && HasTryBlock(step.ThenBranch.ToList()))
                return true;
            if (step.ElseBranch != null && HasTryBlock(step.ElseBranch.ToList()))
                return true;
            if (step.LoopSteps != null && HasTryBlock(step.LoopSteps.ToList()))
                return true;
            if (step.ItemSteps != null && HasTryBlock(step.ItemSteps.ToList()))
                return true;
            if (step.TrySteps != null && HasTryBlock(step.TrySteps.ToList()))
                return true;
        }

        return false;
    }

    private bool HasRiskyOperations(List<FlowStep>? steps)
    {
        if (steps == null)
            return false;

        foreach (var step in steps)
        {
            if (step.Type == StepType.Send || step.Type == StepType.Query ||
                step.Type == StepType.Publish || step.Type == StepType.CallFlow)
            {
                return true;
            }

            if (step.ThenBranch != null && HasRiskyOperations(step.ThenBranch.ToList()))
                return true;
            if (step.ElseBranch != null && HasRiskyOperations(step.ElseBranch.ToList()))
                return true;
            if (step.LoopSteps != null && HasRiskyOperations(step.LoopSteps.ToList()))
                return true;
            if (step.ItemSteps != null && HasRiskyOperations(step.ItemSteps.ToList()))
                return true;
            if (step.TrySteps != null && HasRiskyOperations(step.TrySteps.ToList()))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Analysis result for a single issue found in the flow.
/// </summary>
public class FlowAnalysisResult
{
    public AnalysisSeverity Severity { get; set; }
    public string Message { get; set; }
}

/// <summary>
/// Severity level for analysis results.
/// </summary>
public enum AnalysisSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Report from flow analysis.
/// </summary>
public class FlowAnalysisReport
{
    public bool IsValid { get; set; }
    public List<FlowAnalysisResult> Results { get; set; } = new();

    public int ErrorCount => Results.Count(r => r.Severity == AnalysisSeverity.Error);
    public int WarningCount => Results.Count(r => r.Severity == AnalysisSeverity.Warning);
    public int InfoCount => Results.Count(r => r.Severity == AnalysisSeverity.Info);
}
