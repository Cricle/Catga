using Catga.Abstractions;

namespace Catga.EventSourcing;

/// <summary>
/// Event store interface for event sourcing.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Append events to a stream.
    /// </summary>
    ValueTask AppendAsync(
        string streamId,
        IReadOnlyList<IEvent> events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read events from a stream.
    /// </summary>
    ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current version of a stream.
    /// </summary>
    ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default);

    #region Time Travel API

    /// <summary>
    /// Read events up to a specific version (inclusive).
    /// Used for time travel queries to reconstruct state at a specific version.
    /// </summary>
    ValueTask<EventStream> ReadToVersionAsync(
        string streamId,
        long toVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read events up to a specific timestamp (inclusive).
    /// Used for time travel queries to reconstruct state at a specific point in time.
    /// </summary>
    ValueTask<EventStream> ReadToTimestampAsync(
        string streamId,
        DateTime upperBound,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get version history with timestamps for a stream.
    /// Returns metadata about each version without loading full event data.
    /// </summary>
    ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistoryAsync(
        string streamId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Projection API

    /// <summary>
    /// Get all stream IDs in the event store.
    /// Used for projection catch-up and rebuild.
    /// </summary>
    ValueTask<IReadOnlyList<string>> GetAllStreamIdsAsync(
        CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Event stream containing events and metadata
/// </summary>
public sealed class EventStream
{
    public string StreamId { get; init; } = string.Empty;
    public long Version { get; init; }
    public IReadOnlyList<StoredEvent> Events { get; init; } = Array.Empty<StoredEvent>();
}

/// <summary>
/// Stored event with metadata
/// </summary>
public sealed class StoredEvent
{
    public long Version { get; init; }
    public IEvent Event { get; init; } = null!;
    public DateTime Timestamp { get; init; }
    public string EventType { get; init; } = string.Empty;
}

/// <summary>
/// Version information for time travel queries.
/// </summary>
public sealed class VersionInfo
{
    /// <summary>Event version (0-based).</summary>
    public long Version { get; init; }

    /// <summary>Timestamp when the event was stored.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Event type name.</summary>
    public string EventType { get; init; } = string.Empty;
}

/// <summary>
/// Exception thrown when expected version doesn't match.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    public string StreamId { get; }
    public long ExpectedVersion { get; }
    public long ActualVersion { get; }

    public ConcurrencyException(string streamId, long expectedVersion, long actualVersion)
        : base($"Concurrency conflict on stream '{streamId}'. Expected version {expectedVersion}, but was {actualVersion}")
    {
        StreamId = streamId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}

