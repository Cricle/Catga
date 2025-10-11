namespace Catga.Scheduling;

/// <summary>
/// Message scheduler - schedule messages for delayed or recurring execution
/// </summary>
public interface IMessageScheduler
{
    /// <summary>
    /// Schedule a message to be sent after a delay
    /// </summary>
    ValueTask<ScheduledToken> ScheduleAsync<TMessage>(
        TMessage message,
        TimeSpan delay,
        CancellationToken cancellationToken = default) where TMessage : class;

    /// <summary>
    /// Schedule a message to be sent at a specific time
    /// </summary>
    ValueTask<ScheduledToken> ScheduleAtAsync<TMessage>(
        TMessage message,
        DateTimeOffset scheduledTime,
        CancellationToken cancellationToken = default) where TMessage : class;

    /// <summary>
    /// Schedule a recurring message using cron expression
    /// </summary>
    ValueTask<ScheduledToken> ScheduleRecurringAsync<TMessage>(
        TMessage message,
        string cronExpression,
        CancellationToken cancellationToken = default) where TMessage : class;

    /// <summary>
    /// Cancel a scheduled message
    /// </summary>
    ValueTask<bool> CancelAsync(
        ScheduledToken token,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Token representing a scheduled message
/// </summary>
public readonly record struct ScheduledToken(string Id);

