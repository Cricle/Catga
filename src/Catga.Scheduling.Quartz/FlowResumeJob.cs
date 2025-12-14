using Catga.Flow;
using Catga.Flow.Dsl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Catga.Scheduling.Quartz;

/// <summary>
/// Quartz job that resumes a suspended flow at the scheduled time.
/// </summary>
[DisallowConcurrentExecution]
public sealed class FlowResumeJob(
    IServiceProvider serviceProvider,
    ILogger<FlowResumeJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var data = context.MergedJobDataMap;
        var flowId = data.GetString("flowId");
        var stateId = data.GetString("stateId");
        var scheduleId = data.GetString("scheduleId");

        if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(stateId))
        {
            logger.LogError("FlowResumeJob executed with missing flowId or stateId");
            return;
        }

        logger.LogInformation("Resuming flow {FlowId} for state {StateId} (schedule: {ScheduleId})",
            flowId, stateId, scheduleId);

        try
        {
            // Resolve the flow store and resume service
            var flowStore = serviceProvider.GetRequiredService<IDslFlowStore>();
            var resumeHandler = serviceProvider.GetService<IFlowResumeHandler>();

            if (resumeHandler == null)
            {
                logger.LogWarning("No IFlowResumeHandler registered, flow {FlowId} cannot be resumed automatically", flowId);
                return;
            }

            // Resume the flow
            await resumeHandler.ResumeFlowAsync(flowId, stateId, context.CancellationToken);

            logger.LogInformation("Successfully resumed flow {FlowId} for state {StateId}", flowId, stateId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resume flow {FlowId} for state {StateId}", flowId, stateId);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}

/// <summary>
/// Service interface for resuming flows from scheduled jobs.
/// </summary>
public interface IFlowResumeHandler
{
    ValueTask ResumeFlowAsync(string flowId, string stateId, CancellationToken ct = default);
}
