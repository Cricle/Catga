namespace Catga.Observability;

/// <summary>
/// Interface for Flow DSL metrics collection.
/// Follows Open-Closed principle - extend without modifying existing code.
/// </summary>
public interface IFlowMetrics
{
    /// <summary>Records a flow execution started</summary>
    void RecordFlowStarted(string flowName, string? flowId = null);

    /// <summary>Records a flow execution completed successfully</summary>
    void RecordFlowCompleted(string flowName, string? flowId = null);

    /// <summary>Records a flow execution failed</summary>
    void RecordFlowFailed(string flowName, string? error = null, string? flowId = null);

    /// <summary>Records a step execution started</summary>
    void RecordStepStarted(string flowName, int stepIndex, string stepType);

    /// <summary>Records a step execution completed successfully</summary>
    void RecordStepCompleted(string flowName, int stepIndex, string stepType);

    /// <summary>Records a step execution failed</summary>
    void RecordStepFailed(string flowName, int stepIndex, string stepType, string? error = null);

    /// <summary>Records flow execution duration in milliseconds</summary>
    void RecordFlowDuration(string flowName, double durationMs);

    /// <summary>Records step execution duration in milliseconds</summary>
    void RecordStepDuration(string flowName, int stepIndex, string stepType, double durationMs);
}
