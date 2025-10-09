using System.Collections.Concurrent;
using Catga.Common;
using Catga.Messages;

namespace Catga.EventSourcing;

/// <summary>
/// In-memory event store implementation (for testing/single-instance scenarios)
/// Note: Uses separate dictionary for streams (not inheriting BaseMemoryStore due to different data model)
/// </summary>
public sealed class MemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _streams = new();
    private readonly ConcurrentDictionary<string, object> _locks = new();

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

        var streamLock = _locks.GetOrAdd(streamId, _ => new object());

        lock (streamLock)
        {
            var stream = _streams.GetOrAdd(streamId, _ => new List<StoredEvent>());

            // Check expected version
            if (expectedVersion >= 0 && stream.Count != expectedVersion)
            {
                throw new ConcurrencyException(streamId, expectedVersion, stream.Count);
            }

            var currentVersion = stream.Count;
            var timestamp = DateTime.UtcNow;

            foreach (var @event in events)
            {
                stream.Add(new StoredEvent
                {
                    Version = currentVersion++,
                    Event = @event,
                    Timestamp = timestamp,
                    EventType = @event.GetType().Name
                });
            }
        }

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

        var streamLock = _locks.GetOrAdd(streamId, _ => new object());

        lock (streamLock)
        {
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

        var streamLock = _locks.GetOrAdd(streamId, _ => new object());

        lock (streamLock)
        {
            return ValueTask.FromResult((long)(stream.Count - 1));
        }
    }
}

