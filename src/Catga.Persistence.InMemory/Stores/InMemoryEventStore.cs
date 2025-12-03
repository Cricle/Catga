using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Observability;
using Catga.Resilience;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Catga.Persistence.Stores;

/// <summary>High-performance in-memory event store for testing and single-node scenarios.</summary>
public sealed class InMemoryEventStore(IResiliencePipelineProvider provider) : IEventStore
{
    private readonly ConcurrentDictionary<string, Stream> _streams = new();
    private static readonly KeyValuePair<string, object?> Tag = new("component", "EventStore.InMemory");

    public ValueTask AppendAsync(string streamId, IReadOnlyList<IEvent> events, long expectedVersion = -1, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0) throw new ArgumentException("Events cannot be empty", nameof(events));

        return provider.ExecutePersistenceAsync(c =>
        {
            c.ThrowIfCancellationRequested();
            var sw = Stopwatch.GetTimestamp();
            var stream = _streams.GetOrAdd(streamId, _ => new Stream());
            try
            {
                stream.Append(events, expectedVersion);
                CatgaDiagnostics.EventStoreAppends.Add(1, Tag);
                CatgaDiagnostics.EventStoreAppendDuration.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds, Tag);
            }
            catch (InvalidOperationException)
            {
                CatgaDiagnostics.EventStoreFailures.Add(1, Tag, new("reason", "concurrency"));
                throw new ConcurrencyException(streamId, expectedVersion, stream.Version);
            }
            return ValueTask.CompletedTask;
        }, ct);
    }

    public ValueTask<EventStream> ReadAsync(string streamId, long fromVersion = 0, int maxCount = int.MaxValue, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);
        return provider.ExecutePersistenceAsync(c =>
        {
            c.ThrowIfCancellationRequested();
            var sw = Stopwatch.GetTimestamp();
            EventStream result;
            if (_streams.TryGetValue(streamId, out var stream))
                result = new() { StreamId = streamId, Version = stream.Version, Events = stream.GetEvents(fromVersion, maxCount) };
            else
                result = new() { StreamId = streamId, Version = -1, Events = [] };
            CatgaDiagnostics.EventStoreReads.Add(1, Tag);
            CatgaDiagnostics.EventStoreReadDuration.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds, Tag);
            return new ValueTask<EventStream>(result);
        }, ct);
    }

    public ValueTask<long> GetVersionAsync(string streamId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_streams.TryGetValue(streamId, out var s) ? s.Version : -1L);
    }

    public void Clear() => _streams.Clear();
    public int StreamCount => _streams.Count;
    public int GetEventCount(string streamId) => _streams.TryGetValue(streamId, out var s) ? (int)(s.Version + 1) : 0;

    private sealed class Stream
    {
        private readonly List<StoredEvent> _events = [];
        private readonly Lock _lock = new();
        private long _version = -1;
        public long Version { get { lock (_lock) return _version; } }

        public void Append(IReadOnlyList<IEvent> events, long expected)
        {
            lock (_lock)
            {
                if (expected >= 0 && _version != expected)
                    throw new InvalidOperationException($"Expected {expected}, was {_version}");
                var ts = DateTime.UtcNow;
                foreach (var e in events)
                    _events.Add(new() { Version = ++_version, Event = e, Timestamp = ts, EventType = e.GetType().Name });
            }
        }

        public List<StoredEvent> GetEvents(long from, int max)
        {
            lock (_lock)
            {
                var start = (int)Math.Max(0, from);
                return start >= _events.Count ? [] : _events.GetRange(start, Math.Min(max, _events.Count - start));
            }
        }
    }
}

