namespace Catga.Flow;

/// <summary>
/// Scheduler for Flow resume operations.
/// Integrates with external schedulers (Quartz.NET, Hangfire) to resume suspended flows.
/// </summary>
public interface IFlowScheduler
{
    /// <summary>
    /// Schedule a flow to resume at a specific time.
    /// </summary>
    /// <param name="flowId">Flow identifier</param>
    /// <param name="stateId">State identifier</param>
    /// <param name="resumeAt">Time to resume the flow</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Schedule identifier for tracking/cancellation</returns>
    ValueTask<string> ScheduleResumeAsync(
        string flowId,
        string stateId,
        DateTimeOffset resumeAt,
        CancellationToken ct = default);

    /// <summary>
    /// Cancel a previously scheduled flow resume.
    /// </summary>
    /// <param name="scheduleId">Schedule identifier returned from ScheduleResumeAsync</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if cancelled successfully</returns>
    ValueTask<bool> CancelScheduledResumeAsync(
        string scheduleId,
        CancellationToken ct = default);
}
