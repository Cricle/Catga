using Catga.Messages;

namespace Catga.EventSourcing;

/// <summary>
/// Event store interface for event sourcing
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Append events to a stream
    /// </summary>
    public ValueTask AppendAsync(
        string streamId,
        IEvent[] events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read events from a stream
    /// </summary>
    public ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current version of a stream
    /// </summary>
    public ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default);
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
/// Exception thrown when expected version doesn't match
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

