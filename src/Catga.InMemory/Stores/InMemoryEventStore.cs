using Catga.EventSourcing;
using Catga.Messages;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Catga.InMemory.Stores;

/// <summary>
/// High-performance in-memory event store for testing and single-node scenarios.
/// Lock-free design, zero reflection, optimized for GC.
/// </summary>
public sealed class InMemoryEventStore : IEventStore
{
    // Lock-free concurrent storage
    private readonly ConcurrentDictionary<string, StreamData> _streams = new();

    private sealed class StreamData
    {
        private readonly List<StoredEvent> _events = [];
        private readonly object _lock = new();
        private long _version = -1;

        public long Version
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                lock (_lock) return _version;
            }
        }

        public void Append(IEvent[] events, long expectedVersion)
        {
            lock (_lock)
            {
                if (expectedVersion >= 0 && _version != expectedVersion)
                {
                    throw new InvalidOperationException($"Expected version {expectedVersion}, but was {_version}");
                }

                var timestamp = DateTime.UtcNow;
                foreach (var @event in events)
                {
                    _version++;
                    _events.Add(new StoredEvent
                    {
                        Version = _version,
                        Event = @event,
                        Timestamp = timestamp,
                        EventType = @event.GetType().Name
                    });
                }
            }
        }

        public List<StoredEvent> GetEvents(long fromVersion, int maxCount)
        {
            lock (_lock)
            {
                if (fromVersion < 0) fromVersion = 0;

                var startIndex = (int)fromVersion;
                if (startIndex >= _events.Count)
                    return [];

                var count = Math.Min(maxCount, _events.Count - startIndex);
                return _events.GetRange(startIndex, count);
            }
        }
    }

    public ValueTask AppendAsync(
        string streamId,
        IEvent[] events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(streamId))
            throw new ArgumentException("Stream ID cannot be empty", nameof(streamId));

        if (events == null || events.Length == 0)
            throw new ArgumentException("Events cannot be null or empty", nameof(events));

        cancellationToken.ThrowIfCancellationRequested();

        var stream = _streams.GetOrAdd(streamId, _ => new StreamData());

        try
        {
            stream.Append(events, expectedVersion);
        }
        catch (InvalidOperationException)
        {
            // Convert to ConcurrencyException
            throw new ConcurrencyException(streamId, expectedVersion, stream.Version);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(streamId))
            throw new ArgumentException("Stream ID cannot be empty", nameof(streamId));

        cancellationToken.ThrowIfCancellationRequested();

        if (!_streams.TryGetValue(streamId, out var stream))
        {
            return ValueTask.FromResult(new EventStream
            {
                StreamId = streamId,
                Version = -1,
                Events = Array.Empty<StoredEvent>()
            });
        }

        var events = stream.GetEvents(fromVersion, maxCount);
        var version = stream.Version;

        return ValueTask.FromResult(new EventStream
        {
            StreamId = streamId,
            Version = version,
            Events = events
        });
    }

    public ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(streamId))
            throw new ArgumentException("Stream ID cannot be empty", nameof(streamId));

        cancellationToken.ThrowIfCancellationRequested();

        var version = _streams.TryGetValue(streamId, out var stream)
            ? stream.Version
            : -1L;

        return ValueTask.FromResult(version);
    }

    /// <summary>Clear all streams (for testing)</summary>
    public void Clear() => _streams.Clear();

    /// <summary>Get stream count (for testing)</summary>
    public int StreamCount => _streams.Count;

    /// <summary>Get event count for a stream (for testing)</summary>
    public int GetEventCount(string streamId)
    {
        if (_streams.TryGetValue(streamId, out var stream))
        {
            return (int)(stream.Version + 1);
        }
        return 0;
    }
}

