using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Hosting;
using Catga.Observability;
using Catga.Resilience;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Catga.Persistence.Stores;

/// <summary>High-performance in-memory event store for testing and single-node scenarios.</summary>
public sealed class InMemoryEventStore(IResiliencePipelineProvider provider) : IEventStore, IHealthCheckable, Hosting.IRecoverableComponent
{
    private readonly ConcurrentDictionary<string, Stream> _streams = new();
    private static readonly KeyValuePair<string, object?> Tag = new("component", "EventStore.InMemory");
    private bool _isHealthy = true;
    private string? _healthStatus = "Initialized";
    private DateTimeOffset? _lastHealthCheck;
    
    /// <inheritdoc/>
    public bool IsHealthy => _isHealthy;
    
    /// <inheritdoc/>
    public string? HealthStatus => _healthStatus;
    
    /// <inheritdoc/>
    public DateTimeOffset? LastHealthCheck => _lastHealthCheck;
    
    /// <inheritdoc/>
    public string ComponentName => "InMemoryEventStore";

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
                UpdateHealthStatus(true);
            }
            catch (InvalidOperationException)
            {
                CatgaDiagnostics.EventStoreFailures.Add(1, Tag, new("reason", "concurrency"));
                UpdateHealthStatus(false, "Concurrency exception");
                throw new ConcurrencyException(streamId, expectedVersion, stream.Version);
            }
            catch (Exception ex)
            {
                UpdateHealthStatus(false, ex.Message);
                throw;
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

    #region Time Travel API

    public ValueTask<EventStream> ReadToVersionAsync(string streamId, long toVersion, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);
        return provider.ExecutePersistenceAsync(c =>
        {
            c.ThrowIfCancellationRequested();
            var sw = Stopwatch.GetTimestamp();
            EventStream result;
            if (_streams.TryGetValue(streamId, out var stream))
                result = new() { StreamId = streamId, Version = Math.Min(stream.Version, toVersion), Events = stream.GetEventsToVersion(toVersion) };
            else
                result = new() { StreamId = streamId, Version = -1, Events = [] };
            CatgaDiagnostics.EventStoreReads.Add(1, Tag);
            CatgaDiagnostics.EventStoreReadDuration.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds, Tag);
            return new ValueTask<EventStream>(result);
        }, ct);
    }

    public ValueTask<EventStream> ReadToTimestampAsync(string streamId, DateTime upperBound, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);
        return provider.ExecutePersistenceAsync(c =>
        {
            c.ThrowIfCancellationRequested();
            var sw = Stopwatch.GetTimestamp();
            EventStream result;
            if (_streams.TryGetValue(streamId, out var stream))
            {
                var events = stream.GetEventsToTimestamp(upperBound);
                var version = events.Count > 0 ? events[^1].Version : -1;
                result = new() { StreamId = streamId, Version = version, Events = events };
            }
            else
                result = new() { StreamId = streamId, Version = -1, Events = [] };
            CatgaDiagnostics.EventStoreReads.Add(1, Tag);
            CatgaDiagnostics.EventStoreReadDuration.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds, Tag);
            return new ValueTask<EventStream>(result);
        }, ct);
    }

    public ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistoryAsync(string streamId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);
        ct.ThrowIfCancellationRequested();
        if (_streams.TryGetValue(streamId, out var stream))
            return ValueTask.FromResult(stream.GetVersionHistory());
        return ValueTask.FromResult<IReadOnlyList<VersionInfo>>([]);
    }

    #endregion

    #region Projection API

    public ValueTask<IReadOnlyList<string>> GetAllStreamIdsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<string>>(_streams.Keys.ToList());
    }

    #endregion

    public void Clear() => _streams.Clear();
    public int StreamCount => _streams.Count;
    public int GetEventCount(string streamId) => _streams.TryGetValue(streamId, out var s) ? (int)(s.Version + 1) : 0;

    /// <inheritdoc/>
    public Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // In-memory store doesn't need recovery, just verify it's operational
            _isHealthy = true;
            _healthStatus = "Healthy";
            _lastHealthCheck = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _isHealthy = false;
            _healthStatus = $"Recovery failed: {ex.Message}";
            _lastHealthCheck = DateTimeOffset.UtcNow;
            throw;
        }
    }
    
    /// <summary>
    /// Updates health status based on operation results
    /// </summary>
    private void UpdateHealthStatus(bool success, string? errorMessage = null)
    {
        _isHealthy = success;
        _healthStatus = success ? "Healthy" : $"Unhealthy: {errorMessage}";
        _lastHealthCheck = DateTimeOffset.UtcNow;
    }

    /// <summary>Lock-free stream using immutable snapshots with CAS.</summary>
    private sealed class Stream
    {
        private volatile StoredEvent[] _events = [];
        public long Version => _events.Length - 1;

        public void Append(IReadOnlyList<IEvent> events, long expected)
        {
            while (true)
            {
                var current = _events;
                var currentVersion = current.Length - 1;
                if (expected >= 0 && currentVersion != expected)
                    throw new InvalidOperationException($"Expected {expected}, was {currentVersion}");

                var ts = DateTime.UtcNow;
                var newEvents = new StoredEvent[current.Length + events.Count];
                Array.Copy(current, newEvents, current.Length);
                for (var i = 0; i < events.Count; i++)
                {
                    var e = events[i];
                    newEvents[current.Length + i] = new() { Version = current.Length + i, Event = e, Timestamp = ts, EventType = e.GetType().Name };
                }

                if (Interlocked.CompareExchange(ref _events, newEvents, current) == current)
                    return;
            }
        }

        public List<StoredEvent> GetEvents(long from, int max)
        {
            var snapshot = _events;
            var start = (int)Math.Max(0, from);
            if (start >= snapshot.Length) return [];
            var count = Math.Min(max, snapshot.Length - start);
            return [.. snapshot.AsSpan(start, count)];
        }

        public List<StoredEvent> GetEventsToVersion(long toVersion)
        {
            var snapshot = _events;
            var count = (int)Math.Min(toVersion + 1, snapshot.Length);
            return count <= 0 ? [] : [.. snapshot.AsSpan(0, count)];
        }

        public List<StoredEvent> GetEventsToTimestamp(DateTime upperBound)
        {
            var snapshot = _events;
            var result = new List<StoredEvent>();
            foreach (var e in snapshot)
                if (e.Timestamp <= upperBound) result.Add(e);
            return result;
        }

        public IReadOnlyList<VersionInfo> GetVersionHistory()
        {
            var snapshot = _events;
            var result = new VersionInfo[snapshot.Length];
            for (var i = 0; i < snapshot.Length; i++)
                result[i] = new() { Version = snapshot[i].Version, Timestamp = snapshot[i].Timestamp, EventType = snapshot[i].EventType };
            return result;
        }
    }
}

