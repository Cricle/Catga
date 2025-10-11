namespace Catga.Scheduling;

/// <summary>
/// Internal representation of a scheduled message
/// </summary>
public sealed record ScheduledMessage
{
    public required string Id { get; init; }
    public required string MessageType { get; init; }
    public required byte[] MessageData { get; init; }
    public required DateTimeOffset ScheduledTime { get; init; }
    public string? CronExpression { get; init; }
    public ScheduleStatus Status { get; init; } = ScheduleStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Status of a scheduled message
/// </summary>
public enum ScheduleStatus
{
    Pending = 0,
    Sent = 1,
    Cancelled = 2,
    Failed = 3
}

