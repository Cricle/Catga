using Microsoft.Extensions.Logging;

namespace Catga.Observability;

/// <summary>
/// Structured logging for Flow DSL operations.
/// Provides consistent log messages with proper context.
/// </summary>
public static class FlowLogger
{
    // ========== Flow Lifecycle Logs ==========

    public static void LogFlowStarted(ILogger logger, string flowName, string? flowId = null)
    {
        logger.LogInformation("Flow {FlowName} started. FlowId={FlowId}", flowName, flowId ?? "N/A");
    }

    public static void LogFlowCompleted(ILogger logger, string flowName, double durationMs, string? flowId = null)
    {
        logger.LogInformation("Flow {FlowName} completed in {DurationMs}ms. FlowId={FlowId}",
            flowName, durationMs, flowId ?? "N/A");
    }

    public static void LogFlowFailed(ILogger logger, string flowName, string error, double durationMs, string? flowId = null)
    {
        logger.LogError("Flow {FlowName} failed after {DurationMs}ms. Error={Error}, FlowId={FlowId}",
            flowName, durationMs, error, flowId ?? "N/A");
    }

    public static void LogFlowResumed(ILogger logger, string flowName, int fromStepIndex, string? flowId = null)
    {
        logger.LogInformation("Flow {FlowName} resumed from step {StepIndex}. FlowId={FlowId}",
            flowName, fromStepIndex, flowId ?? "N/A");
    }

    // ========== Step Lifecycle Logs ==========

    public static void LogStepStarted(ILogger logger, string flowName, int stepIndex, string stepType, string? tag = null)
    {
        logger.LogDebug("Flow {FlowName} step {StepIndex} ({StepType}) started. Tag={Tag}",
            flowName, stepIndex, stepType, tag ?? "N/A");
    }

    public static void LogStepCompleted(ILogger logger, string flowName, int stepIndex, string stepType, double durationMs)
    {
        logger.LogDebug("Flow {FlowName} step {StepIndex} ({StepType}) completed in {DurationMs}ms",
            flowName, stepIndex, stepType, durationMs);
    }

    public static void LogStepFailed(ILogger logger, string flowName, int stepIndex, string stepType, string error)
    {
        logger.LogWarning("Flow {FlowName} step {StepIndex} ({StepType}) failed. Error={Error}",
            flowName, stepIndex, stepType, error);
    }

    public static void LogStepSkipped(ILogger logger, string flowName, int stepIndex, string stepType, string reason)
    {
        logger.LogDebug("Flow {FlowName} step {StepIndex} ({StepType}) skipped. Reason={Reason}",
            flowName, stepIndex, stepType, reason);
    }

    public static void LogStepRetried(ILogger logger, string flowName, int stepIndex, string stepType, int retryCount, string error)
    {
        logger.LogWarning("Flow {FlowName} step {StepIndex} ({StepType}) retry {RetryCount}. Error={Error}",
            flowName, stepIndex, stepType, retryCount, error);
    }

    // ========== Branch Logs ==========

    public static void LogBranchEntered(ILogger logger, string flowName, string branchType, int branchIndex)
    {
        logger.LogDebug("Flow {FlowName} entered {BranchType} branch {BranchIndex}",
            flowName, branchType, branchIndex);
    }

    // ========== ForEach Logs ==========

    public static void LogForEachStarted(ILogger logger, string flowName, int stepIndex, int totalItems, int parallelism)
    {
        logger.LogDebug("Flow {FlowName} step {StepIndex} ForEach started. Items={TotalItems}, Parallelism={Parallelism}",
            flowName, stepIndex, totalItems, parallelism);
    }

    public static void LogForEachCompleted(ILogger logger, string flowName, int stepIndex, int processedItems, double durationMs)
    {
        logger.LogDebug("Flow {FlowName} step {StepIndex} ForEach completed. Processed={ProcessedItems} in {DurationMs}ms",
            flowName, stepIndex, processedItems, durationMs);
    }
}
