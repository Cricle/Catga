using System.Diagnostics;

namespace Catga.Observability;

/// <summary>
/// ActivitySource for Flow DSL distributed tracing.
/// Extends CatgaActivitySource pattern - DRY principle.
/// </summary>
public static class FlowActivitySource
{
    public const string SourceName = "Catga.Flow";
    public const string Version = "1.0.0";

    public static readonly ActivitySource Source = new(SourceName, Version);

    /// <summary>Activity tag keys for Flow operations</summary>
    public static class Tags
    {
        public const string FlowName = "catga.flow.name";
        public const string FlowId = "catga.flow.id";
        public const string FlowStatus = "catga.flow.status";

        public const string StepIndex = "catga.flow.step.index";
        public const string StepType = "catga.flow.step.type";
        public const string StepTag = "catga.flow.step.tag";
        public const string StepStatus = "catga.flow.step.status";

        public const string BranchType = "catga.flow.branch.type";
        public const string BranchIndex = "catga.flow.branch.index";

        public const string ForEachItemIndex = "catga.flow.foreach.item_index";
        public const string ForEachTotalItems = "catga.flow.foreach.total_items";
        public const string ForEachParallelism = "catga.flow.foreach.parallelism";

        public const string Error = "catga.flow.error";
        public const string ErrorType = "catga.flow.error.type";

        public const string Duration = "catga.flow.duration.ms";
        public const string RetryCount = "catga.flow.retry.count";
    }

    /// <summary>Activity event names for Flow timeline markers</summary>
    public static class Events
    {
        public const string FlowStarted = "catga.flow.started";
        public const string FlowCompleted = "catga.flow.completed";
        public const string FlowFailed = "catga.flow.failed";
        public const string FlowResumed = "catga.flow.resumed";

        public const string StepStarted = "catga.flow.step.started";
        public const string StepCompleted = "catga.flow.step.completed";
        public const string StepFailed = "catga.flow.step.failed";
        public const string StepSkipped = "catga.flow.step.skipped";
        public const string StepRetried = "catga.flow.step.retried";

        public const string BranchEntered = "catga.flow.branch.entered";
        public const string BranchExited = "catga.flow.branch.exited";

        public const string ForEachStarted = "catga.flow.foreach.started";
        public const string ForEachItemProcessed = "catga.flow.foreach.item_processed";
        public const string ForEachCompleted = "catga.flow.foreach.completed";
    }

    /// <summary>Start a flow execution activity</summary>
    public static Activity? StartFlowActivity(string flowName, string? flowId = null)
    {
        var activity = Source.StartActivity(flowName, ActivityKind.Internal);
        activity?.SetTag(Tags.FlowName, flowName);
        if (flowId != null) activity?.SetTag(Tags.FlowId, flowId);
        return activity;
    }

    /// <summary>Start a step execution activity</summary>
    public static Activity? StartStepActivity(string flowName, int stepIndex, string stepType)
    {
        var activity = Source.StartActivity($"{flowName}.Step{stepIndex}", ActivityKind.Internal);
        activity?.SetTag(Tags.FlowName, flowName);
        activity?.SetTag(Tags.StepIndex, stepIndex);
        activity?.SetTag(Tags.StepType, stepType);
        return activity;
    }
}
