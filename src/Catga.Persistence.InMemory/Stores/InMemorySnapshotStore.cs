using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.EventSourcing;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>In-memory snapshot store for development/testing (single snapshot per stream).</summary>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly ConcurrentDictionary<string, (object State, long Ver, DateTime At)> _data = new();

    public ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId, TAggregate aggregate, long version, CancellationToken ct = default) where TAggregate : class
    {
        _data[streamId] = (aggregate, version, DateTime.UtcNow);
        return ValueTask.CompletedTask;
    }

    public ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId, CancellationToken ct = default) where TAggregate : class
        => ValueTask.FromResult(_data.TryGetValue(streamId, out var e) && e.State is TAggregate s
            ? new Snapshot<TAggregate> { StreamId = streamId, State = s, Version = e.Ver, Timestamp = e.At } : (Snapshot<TAggregate>?)null);

    public ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
    {
        _data.TryRemove(streamId, out _);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Enhanced in-memory snapshot store that keeps multiple versions.
/// Supports time travel queries and snapshot cleanup.
/// </summary>
public sealed class EnhancedInMemorySnapshotStore : IEnhancedSnapshotStore
{
    private readonly Dictionary<string, List<(object State, long Version, DateTime Timestamp)>> _snapshots = new();
    private readonly object _lock = new();

    public ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        TAggregate aggregate,
        long version,
        CancellationToken ct = default) where TAggregate : class
    {
        lock (_lock)
        {
            if (!_snapshots.TryGetValue(streamId, out var list))
            {
                list = new List<(object, long, DateTime)>();
                _snapshots[streamId] = list;
            }
            list.Add((aggregate, version, DateTime.UtcNow));
            list.Sort((a, b) => a.Version.CompareTo(b.Version));
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        CancellationToken ct = default) where TAggregate : class
    {
        lock (_lock)
        {
            if (_snapshots.TryGetValue(streamId, out var list) && list.Count > 0)
            {
                var latest = list[^1];
                if (latest.State is TAggregate state)
                {
                    return ValueTask.FromResult<Snapshot<TAggregate>?>(new Snapshot<TAggregate>
                    {
                        StreamId = streamId,
                        State = state,
                        Version = latest.Version,
                        Timestamp = latest.Timestamp
                    });
                }
            }
        }
        return ValueTask.FromResult<Snapshot<TAggregate>?>(null);
    }

    public ValueTask<Snapshot<TAggregate>?> LoadAtVersionAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        long version,
        CancellationToken ct = default) where TAggregate : class
    {
        lock (_lock)
        {
            if (_snapshots.TryGetValue(streamId, out var list))
            {
                var snapshot = list
                    .Where(s => s.Version <= version)
                    .OrderByDescending(s => s.Version)
                    .FirstOrDefault();

                if (snapshot.State is TAggregate state)
                {
                    return ValueTask.FromResult<Snapshot<TAggregate>?>(new Snapshot<TAggregate>
                    {
                        StreamId = streamId,
                        State = state,
                        Version = snapshot.Version,
                        Timestamp = snapshot.Timestamp
                    });
                }
            }
        }
        return ValueTask.FromResult<Snapshot<TAggregate>?>(null);
    }

    public ValueTask<IReadOnlyList<SnapshotInfo>> GetSnapshotHistoryAsync(
        string streamId,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_snapshots.TryGetValue(streamId, out var list))
            {
                var history = list
                    .Select(s => new SnapshotInfo(s.Version, s.Timestamp))
                    .ToList();
                return ValueTask.FromResult<IReadOnlyList<SnapshotInfo>>(history);
            }
        }
        return ValueTask.FromResult<IReadOnlyList<SnapshotInfo>>(Array.Empty<SnapshotInfo>());
    }

    public ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _snapshots.Remove(streamId);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteBeforeVersionAsync(
        string streamId,
        long version,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_snapshots.TryGetValue(streamId, out var list))
            {
                list.RemoveAll(s => s.Version < version);
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask CleanupAsync(
        string streamId,
        int keepCount,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_snapshots.TryGetValue(streamId, out var list) && list.Count > keepCount)
            {
                var toRemove = list.Count - keepCount;
                list.RemoveRange(0, toRemove);
            }
        }
        return ValueTask.CompletedTask;
    }
}
