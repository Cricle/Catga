using Catga.Debugger.Models;

namespace Catga.Debugger.Storage;

/// <summary>Event store for replay - supports time-range queries</summary>
public interface IEventStore
{
    /// <summary>Save events to store</summary>
    ValueTask SaveAsync(IEnumerable<ReplayableEvent> events, CancellationToken cancellationToken = default);

    /// <summary>Get events by time range</summary>
    ValueTask<IEnumerable<ReplayableEvent>> GetEventsAsync(
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default);

    /// <summary>Get events by correlation ID</summary>
    ValueTask<IEnumerable<ReplayableEvent>> GetEventsByCorrelationAsync(
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Get event by ID</summary>
    ValueTask<ReplayableEvent?> GetEventByIdAsync(
        string eventId,
        CancellationToken cancellationToken = default);

    /// <summary>Clean up old events</summary>
    ValueTask CleanupAsync(DateTime olderThan, CancellationToken cancellationToken = default);

    /// <summary>Get statistics</summary>
    ValueTask<EventStoreStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Event store statistics</summary>
public sealed class EventStoreStats
{
    public long TotalEvents { get; init; }
    public long TotalFlows { get; init; }
    public long StorageSizeBytes { get; init; }
    public DateTime OldestEvent { get; init; }
    public DateTime NewestEvent { get; init; }
}

