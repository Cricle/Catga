using Catga.Flow;
using Hangfire;

namespace Catga.Scheduling.Hangfire;

/// <summary>
/// Hangfire-based implementation of IFlowScheduler.
/// Schedules flow resume operations using Hangfire background jobs.
/// </summary>
public sealed class HangfireFlowScheduler(IBackgroundJobClient jobClient) : IFlowScheduler
{
    public ValueTask<string> ScheduleResumeAsync(
        string flowId,
        string stateId,
        DateTimeOffset resumeAt,
        CancellationToken ct = default)
    {
        var delay = resumeAt - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
            delay = TimeSpan.Zero;

        var jobId = jobClient.Schedule<FlowResumeService>(
            service => service.ResumeFlowAsync(flowId, stateId, CancellationToken.None),
            delay);

        return ValueTask.FromResult(jobId);
    }

    public ValueTask<bool> CancelScheduledResumeAsync(
        string scheduleId,
        CancellationToken ct = default)
    {
        var deleted = BackgroundJob.Delete(scheduleId);
        return ValueTask.FromResult(deleted);
    }
}
