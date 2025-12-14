using Microsoft.Extensions.Logging;

namespace Catga.Flow.Dsl;

/// <summary>
/// Default implementation of IFlowResumeHandler that resumes flows from their stored snapshots.
/// </summary>
public sealed class DefaultFlowResumeHandler(
    IDslFlowStore store,
    ILogger<DefaultFlowResumeHandler> logger) : IFlowResumeHandler
{
    public async ValueTask ResumeFlowAsync(string flowId, string stateId, CancellationToken ct = default)
    {
        logger.LogInformation("Attempting to resume flow {FlowId}", flowId);

        // Get the wait condition to find the flow type and step
        var correlationId = $"{flowId}-step-";

        // Find wait conditions for this flow
        var timedOut = await store.GetTimedOutWaitConditionsAsync(ct);
        var waitCondition = timedOut.FirstOrDefault(w => w.FlowId == flowId);

        if (waitCondition == null)
        {
            // Try to find by direct correlation pattern
            for (int i = 0; i < 100; i++) // Check up to 100 steps
            {
                var condition = await store.GetWaitConditionAsync($"{flowId}-step-{i}", ct);
                if (condition != null && condition.ScheduleId != null)
                {
                    waitCondition = condition;
                    break;
                }
            }
        }

        if (waitCondition == null)
        {
            logger.LogWarning("No wait condition found for flow {FlowId}, cannot resume", flowId);
            return;
        }

        // Mark the wait condition as completed
        var updatedCondition = waitCondition with { CompletedCount = waitCondition.ExpectedCount };
        await store.UpdateWaitConditionAsync($"{flowId}-step-{waitCondition.Step}", updatedCondition, ct);

        logger.LogInformation("Flow {FlowId} wait condition updated, ready for resume at step {Step}",
            flowId, waitCondition.Step);

        // Note: The actual flow resumption is handled by the FlowResumeHandler
        // which listens for FlowCompletedEvent. For scheduled resumes, we need
        // to trigger the flow execution directly.

        // Clear the wait condition
        await store.ClearWaitConditionAsync($"{flowId}-step-{waitCondition.Step}", ct);
    }
}
