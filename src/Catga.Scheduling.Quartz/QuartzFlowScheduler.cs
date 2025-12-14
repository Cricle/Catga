using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using Quartz;

namespace Catga.Scheduling.Quartz;

/// <summary>
/// Quartz.NET-based implementation of IFlowScheduler.
/// Schedules flow resume operations using Quartz jobs.
/// </summary>
public sealed class QuartzFlowScheduler(
    ISchedulerFactory schedulerFactory,
    IMessageSerializer serializer) : IFlowScheduler
{
    public async ValueTask<string> ScheduleResumeAsync(
        string flowId,
        string stateId,
        DateTimeOffset resumeAt,
        CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var scheduleId = GenerateScheduleId();

        var jobData = new JobDataMap
        {
            ["flowId"] = flowId,
            ["stateId"] = stateId,
            ["scheduleId"] = scheduleId
        };

        var job = JobBuilder.Create<FlowResumeJob>()
            .WithIdentity(scheduleId, "catga-flow-resume")
            .SetJobData(jobData)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{scheduleId}-trigger", "catga-flow-resume")
            .StartAt(resumeAt)
            .Build();

        await scheduler.ScheduleJob(job, trigger, ct);

        return scheduleId;
    }

    public async ValueTask<bool> CancelScheduledResumeAsync(
        string scheduleId,
        CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var jobKey = new JobKey(scheduleId, "catga-flow-resume");
        return await scheduler.DeleteJob(jobKey, ct);
    }

    private static string GenerateScheduleId()
        => $"flow-resume-{Guid.NewGuid():N}";
}
