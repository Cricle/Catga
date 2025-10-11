using System.Collections.Concurrent;
using Catga.Common;
using Catga.Messages;

namespace Catga.EventSourcing;

/// <summary>
/// In-memory event store implementation (for testing/single-instance scenarios)
/// Note: Uses separate dictionary for streams (not inheriting BaseMemoryStore due to different data model)
/// </summary>
public sealed class MemoryEventStore : IEventStore, IDisposable
{
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _streams = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async ValueTask AppendAsync(
        string streamId,
        IEvent[] events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Length == 0)
        {
            return;
        }

        var streamLock = _locks.GetOrAdd(streamId, _ => new SemaphoreSlim(1, 1));

        await streamLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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
        finally
        {
            streamLock.Release();
        }
    }

    public async ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        if (!_streams.TryGetValue(streamId, out var stream))
        {
            return new EventStream
            {
                StreamId = streamId,
                Version = -1,
                Events = Array.Empty<StoredEvent>()
            };
        }

        var streamLock = _locks.GetOrAdd(streamId, _ => new SemaphoreSlim(1, 1));

        await streamLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var events = stream
                .Where(e => e.Version >= fromVersion)
                .Take(maxCount)
                .ToArray();

            return new EventStream
            {
                StreamId = streamId,
                Version = stream.Count - 1,
                Events = events
            };
        }
        finally
        {
            streamLock.Release();
        }
    }

    public async ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        if (!_streams.TryGetValue(streamId, out var stream))
        {
            return -1L;
        }

        var streamLock = _locks.GetOrAdd(streamId, _ => new SemaphoreSlim(1, 1));

        await streamLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return stream.Count - 1;
        }
        finally
        {
            streamLock.Release();
        }
    }

    public void Dispose()
    {
        foreach (var semaphore in _locks.Values)
        {
            semaphore?.Dispose();
        }
        _locks.Clear();
    }
}
