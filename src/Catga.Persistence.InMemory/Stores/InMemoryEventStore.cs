using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Observability;
using Catga.Resilience;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Catga.Persistence.Stores;

/// <summary>
/// High-performance in-memory event store for testing and single-node scenarios.
/// Lock-free design, zero reflection, optimized for GC.
/// </summary>
public sealed class InMemoryEventStore : IEventStore
{
    // Lock-free concurrent storage
    private readonly ConcurrentDictionary<string, StreamData> _streams = new();
    private readonly IResiliencePipelineProvider _provider;

    public InMemoryEventStore(IResiliencePipelineProvider? provider = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

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

        public void Append(IReadOnlyList<IEvent> events, long expectedVersion)
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
        IReadOnlyList<IEvent> events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        return _provider.ExecutePersistenceAsync(ct =>
        {
            if (string.IsNullOrEmpty(streamId))
                throw new ArgumentException("Stream ID cannot be empty", nameof(streamId));

            if (events == null || events.Count == 0)
                throw new ArgumentException("Events cannot be null or empty", nameof(events));

            ct.ThrowIfCancellationRequested();

            var startTimestamp = Stopwatch.GetTimestamp();
            var stream = _streams.GetOrAdd(streamId, _ => new StreamData());

            try
            {
                stream.Append(events, expectedVersion);

                var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
                var durationMs = elapsed * 1000.0 / Stopwatch.Frequency;
                var tag_component = new KeyValuePair<string, object?>("component", "EventStore.InMemory");
                CatgaDiagnostics.EventStoreAppends.Add(1, tag_component);
                CatgaDiagnostics.EventStoreAppendDuration.Record(durationMs, tag_component);
            }
            catch (InvalidOperationException)
            {
                CatgaDiagnostics.EventStoreFailures.Add(1,
                    new KeyValuePair<string, object?>("component", "EventStore.InMemory"),
                    new KeyValuePair<string, object?>("reason", "concurrency"));
                throw new ConcurrencyException(streamId, expectedVersion, stream.Version);
            }
            catch
            {
                CatgaDiagnostics.EventStoreFailures.Add(1,
                    new KeyValuePair<string, object?>("component", "EventStore.InMemory"),
                    new KeyValuePair<string, object?>("reason", "exception"));
                throw;
            }
            return ValueTask.CompletedTask;
        }, cancellationToken);
    }

    public ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return _provider.ExecutePersistenceAsync(ct =>
        {
            if (string.IsNullOrEmpty(streamId))
                throw new ArgumentException("Stream ID cannot be empty", nameof(streamId));

            ct.ThrowIfCancellationRequested();

            var startTimestamp = Stopwatch.GetTimestamp();

            if (!_streams.TryGetValue(streamId, out var stream))
            {
                var emptyResult = new EventStream
                {
                    StreamId = streamId,
                    Version = -1,
                    Events = Array.Empty<StoredEvent>()
                };

                var elapsedEmpty = Stopwatch.GetTimestamp() - startTimestamp;
                var durationMsEmpty = elapsedEmpty * 1000.0 / Stopwatch.Frequency;
                var tag_component = new KeyValuePair<string, object?>("component", "EventStore.InMemory");
                CatgaDiagnostics.EventStoreReads.Add(1, tag_component);
                CatgaDiagnostics.EventStoreReadDuration.Record(durationMsEmpty, tag_component);
                return new ValueTask<EventStream>(emptyResult);
            }

            var events = stream.GetEvents(fromVersion, maxCount);
            var version = stream.Version;

            var result = new EventStream
            {
                StreamId = streamId,
                Version = version,
                Events = events
            };

            var tag = new KeyValuePair<string, object?>("component", "EventStore.InMemory");
            CatgaDiagnostics.EventStoreReads.Add(1, tag);
            CatgaDiagnostics.EventStoreReadDuration.Record((double)((Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency), tag);
            return new ValueTask<EventStream>(result);
        }, cancellationToken);
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
    public int GetEventCount(string streamId) => _streams.TryGetValue(streamId, out var stream) ? (int)(stream.Version + 1) : 0;
}

