using System.Collections.Concurrent;
using System.Collections.Immutable;
using Catga.Common;
using Catga.Messages;

namespace Catga.EventSourcing;

/// <summary>
/// In-memory event store implementation (for testing/single-instance scenarios)
/// Lock-free using ImmutableList for thread-safe append operations
/// </summary>
public sealed class MemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<string, ImmutableList<StoredEvent>> _streams = new();

    public ValueTask AppendAsync(
        string streamId,
        IEvent[] events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Length == 0)
        {
            return ValueTask.CompletedTask;
        }

        var timestamp = DateTime.UtcNow;

        _streams.AddOrUpdate(
            streamId,
            _ =>
            {
                // New stream: create initial event list
                var newEvents = events.Select((e, i) => new StoredEvent
                {
                    Version = i,
                    Event = e,
                    Timestamp = timestamp,
                    EventType = e.GetType().Name
                }).ToImmutableList();
                return newEvents;
            },
            (_, existing) =>
            {
                // Optimistic concurrency check
                if (expectedVersion >= 0 && existing.Count != expectedVersion)
                {
                    throw new ConcurrencyException(streamId, expectedVersion, existing.Count);
                }

                // Append events (immutable operation)
                var baseVersion = existing.Count;
                var newEvents = events.Select((e, i) => new StoredEvent
                {
                    Version = baseVersion + i,
                    Event = e,
                    Timestamp = timestamp,
                    EventType = e.GetType().Name
                });

                return existing.AddRange(newEvents);
            });

        return ValueTask.CompletedTask;
    }

    public ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        if (!_streams.TryGetValue(streamId, out var stream))
        {
            return ValueTask.FromResult(new EventStream
            {
                StreamId = streamId,
                Version = -1,
                Events = Array.Empty<StoredEvent>()
            });
        }

        var events = stream
            .Where(e => e.Version >= fromVersion)
            .Take(maxCount)
            .ToArray();

        return ValueTask.FromResult(new EventStream
        {
            StreamId = streamId,
            Version = stream.Count - 1,
            Events = events
        });
    }

    public ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        if (!_streams.TryGetValue(streamId, out var stream))
        {
            return ValueTask.FromResult(-1L);
        }

        return ValueTask.FromResult((long)(stream.Count - 1));
    }
}
