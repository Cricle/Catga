using Catga.Flow;
using Microsoft.Extensions.Logging;

namespace Catga.Scheduling.Hangfire;

/// <summary>
/// Hangfire service that resumes a suspended flow at the scheduled time.
/// </summary>
public sealed class FlowResumeService(
    IFlowResumeHandler? resumeHandler,
    ILogger<FlowResumeService> logger)
{
    public async Task ResumeFlowAsync(string flowId, string stateId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(stateId))
        {
            logger.LogError("FlowResumeService called with missing flowId or stateId");
            return;
        }

        logger.LogInformation("Resuming flow {FlowId} for state {StateId}", flowId, stateId);

        if (resumeHandler == null)
        {
            logger.LogWarning("No IFlowResumeHandler registered, flow {FlowId} cannot be resumed automatically", flowId);
            return;
        }

        try
        {
            await resumeHandler.ResumeFlowAsync(flowId, stateId, ct);
            logger.LogInformation("Successfully resumed flow {FlowId} for state {StateId}", flowId, stateId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resume flow {FlowId} for state {StateId}", flowId, stateId);
            throw;
        }
    }
}
