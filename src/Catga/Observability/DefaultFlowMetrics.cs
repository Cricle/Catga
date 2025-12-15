using System.Diagnostics;

namespace Catga.Observability;

/// <summary>
/// Default implementation of IFlowMetrics using FlowDiagnostics.
/// Integrates metrics, tracing, and provides extension points.
/// </summary>
public sealed class DefaultFlowMetrics : IFlowMetrics
{
    public static readonly DefaultFlowMetrics Instance = new();

    public void RecordFlowStarted(string flowName, string? flowId = null)
    {
        FlowDiagnostics.FlowsStarted.Add(1, new KeyValuePair<string, object?>("flow.name", flowName));
        FlowDiagnostics.IncrementActiveFlows();

        Activity.Current?.AddEvent(new ActivityEvent(FlowActivitySource.Events.FlowStarted,
            tags: new ActivityTagsCollection
            {
                { FlowActivitySource.Tags.FlowName, flowName },
                { FlowActivitySource.Tags.FlowId, flowId }
            }));
    }

    public void RecordFlowCompleted(string flowName, string? flowId = null)
    {
        FlowDiagnostics.FlowsCompleted.Add(1, new KeyValuePair<string, object?>("flow.name", flowName));
        FlowDiagnostics.DecrementActiveFlows();

        Activity.Current?.AddEvent(new ActivityEvent(FlowActivitySource.Events.FlowCompleted,
            tags: new ActivityTagsCollection
            {
                { FlowActivitySource.Tags.FlowName, flowName },
                { FlowActivitySource.Tags.FlowId, flowId },
                { FlowActivitySource.Tags.FlowStatus, "completed" }
            }));
    }

    public void RecordFlowFailed(string flowName, string? error = null, string? flowId = null)
    {
        FlowDiagnostics.FlowsFailed.Add(1, new KeyValuePair<string, object?>("flow.name", flowName));
        FlowDiagnostics.DecrementActiveFlows();

        Activity.Current?.AddEvent(new ActivityEvent(FlowActivitySource.Events.FlowFailed,
            tags: new ActivityTagsCollection
            {
                { FlowActivitySource.Tags.FlowName, flowName },
                { FlowActivitySource.Tags.FlowId, flowId },
                { FlowActivitySource.Tags.FlowStatus, "failed" },
                { FlowActivitySource.Tags.Error, error }
            }));
    }

    public void RecordStepStarted(string flowName, int stepIndex, string stepType)
    {
        FlowDiagnostics.StepsExecuted.Add(1,
            new KeyValuePair<string, object?>("flow.name", flowName),
            new KeyValuePair<string, object?>("step.type", stepType));

        Activity.Current?.AddEvent(new ActivityEvent(FlowActivitySource.Events.StepStarted,
            tags: new ActivityTagsCollection
            {
                { FlowActivitySource.Tags.FlowName, flowName },
                { FlowActivitySource.Tags.StepIndex, stepIndex },
                { FlowActivitySource.Tags.StepType, stepType }
            }));
    }

    public void RecordStepCompleted(string flowName, int stepIndex, string stepType)
    {
        FlowDiagnostics.StepsSucceeded.Add(1,
            new KeyValuePair<string, object?>("flow.name", flowName),
            new KeyValuePair<string, object?>("step.type", stepType));

        Activity.Current?.AddEvent(new ActivityEvent(FlowActivitySource.Events.StepCompleted,
            tags: new ActivityTagsCollection
            {
                { FlowActivitySource.Tags.FlowName, flowName },
                { FlowActivitySource.Tags.StepIndex, stepIndex },
                { FlowActivitySource.Tags.StepType, stepType },
                { FlowActivitySource.Tags.StepStatus, "completed" }
            }));
    }

    public void RecordStepFailed(string flowName, int stepIndex, string stepType, string? error = null)
    {
        FlowDiagnostics.StepsFailed.Add(1,
            new KeyValuePair<string, object?>("flow.name", flowName),
            new KeyValuePair<string, object?>("step.type", stepType));

        Activity.Current?.AddEvent(new ActivityEvent(FlowActivitySource.Events.StepFailed,
            tags: new ActivityTagsCollection
            {
                { FlowActivitySource.Tags.FlowName, flowName },
                { FlowActivitySource.Tags.StepIndex, stepIndex },
                { FlowActivitySource.Tags.StepType, stepType },
                { FlowActivitySource.Tags.StepStatus, "failed" },
                { FlowActivitySource.Tags.Error, error }
            }));
    }

    public void RecordFlowDuration(string flowName, double durationMs)
    {
        FlowDiagnostics.FlowDuration.Record(durationMs,
            new KeyValuePair<string, object?>("flow.name", flowName));

        Activity.Current?.SetTag(FlowActivitySource.Tags.Duration, durationMs);
    }

    public void RecordStepDuration(string flowName, int stepIndex, string stepType, double durationMs)
    {
        FlowDiagnostics.StepDuration.Record(durationMs,
            new KeyValuePair<string, object?>("flow.name", flowName),
            new KeyValuePair<string, object?>("step.type", stepType));
    }
}
