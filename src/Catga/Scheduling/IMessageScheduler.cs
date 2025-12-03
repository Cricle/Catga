using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Scheduling;

/// <summary>
/// Message scheduler for delayed/scheduled message delivery.
/// AOT-compatible, zero-allocation design.
/// </summary>
public interface IMessageScheduler
{
    /// <summary>Schedule a message for future delivery.</summary>
    ValueTask<ScheduledMessageHandle> ScheduleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        DateTimeOffset deliverAt,
        CancellationToken ct = default) where TMessage : class, IMessage;

    /// <summary>Schedule a message with relative delay.</summary>
    ValueTask<ScheduledMessageHandle> ScheduleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TimeSpan delay,
        CancellationToken ct = default) where TMessage : class, IMessage;

    /// <summary>Cancel a scheduled message.</summary>
    ValueTask<bool> CancelAsync(string scheduleId, CancellationToken ct = default);

    /// <summary>Get scheduled message info.</summary>
    ValueTask<ScheduledMessageInfo?> GetAsync(string scheduleId, CancellationToken ct = default);

    /// <summary>List pending scheduled messages.</summary>
    IAsyncEnumerable<ScheduledMessageInfo> ListPendingAsync(
        int limit = 100,
        CancellationToken ct = default);
}

/// <summary>Handle to a scheduled message.</summary>
public readonly record struct ScheduledMessageHandle
{
    /// <summary>Unique schedule identifier.</summary>
    public required string ScheduleId { get; init; }

    /// <summary>When the message will be delivered.</summary>
    public required DateTimeOffset DeliverAt { get; init; }

    /// <summary>Message type name.</summary>
    public required string MessageType { get; init; }
}

/// <summary>Information about a scheduled message.</summary>
public readonly record struct ScheduledMessageInfo
{
    public required string ScheduleId { get; init; }
    public required string MessageType { get; init; }
    public required DateTimeOffset DeliverAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required ScheduledMessageStatus Status { get; init; }
    public int RetryCount { get; init; }
    public string? LastError { get; init; }
}

/// <summary>Scheduled message status.</summary>
public enum ScheduledMessageStatus : byte
{
    Pending = 0,
    Processing = 1,
    Delivered = 2,
    Cancelled = 3,
    Failed = 4
}

/// <summary>Scheduler options.</summary>
public sealed class MessageSchedulerOptions
{
    /// <summary>Polling interval for due messages.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Batch size for processing due messages.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Maximum retry count for failed deliveries.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Delay between retries.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Key prefix for scheduled messages.</summary>
    public string KeyPrefix { get; set; } = "catga:schedule:";
}
