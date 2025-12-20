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
        => ValueTask.FromResult<Snapshot<TAggregate>?>(_data.TryGetValue(streamId, out var e) && e.State is TAggregate s
            ? new Snapshot<TAggregate> { StreamId = streamId, State = s, Version = e.Ver, Timestamp = e.At } : null);

    public ValueTask DeleteAsync(string streamId, CancellationToken ct = default) { _data.TryRemove(streamId, out _); return ValueTask.CompletedTask; }
}

/// <summary>Enhanced in-memory snapshot store with multiple versions and time travel support.</summary>
public sealed class EnhancedInMemorySnapshotStore : IEnhancedSnapshotStore
{
    private readonly ConcurrentDictionary<string, List<(object State, long Version, DateTime Timestamp)>> _snapshots = new();

    public ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId, TAggregate aggregate, long version, CancellationToken ct = default) where TAggregate : class
    {
        var list = _snapshots.GetOrAdd(streamId, _ => []);
        lock (list)
        {
            list.Add((aggregate, version, DateTime.UtcNow));
            list.Sort((a, b) => a.Version.CompareTo(b.Version));
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId, CancellationToken ct = default) where TAggregate : class
    {
        if (_snapshots.TryGetValue(streamId, out var list))
        {
            lock (list)
            {
                if (list.Count > 0 && list[^1].State is TAggregate state)
                    return ValueTask.FromResult<Snapshot<TAggregate>?>(new() { StreamId = streamId, State = state, Version = list[^1].Version, Timestamp = list[^1].Timestamp });
            }
        }
        return ValueTask.FromResult<Snapshot<TAggregate>?>(null);
    }

    public ValueTask<Snapshot<TAggregate>?> LoadAtVersionAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId, long version, CancellationToken ct = default) where TAggregate : class
    {
        if (_snapshots.TryGetValue(streamId, out var list))
        {
            lock (list)
            {
                var s = list.Where(x => x.Version <= version).OrderByDescending(x => x.Version).FirstOrDefault();
                if (s.State is TAggregate state)
                    return ValueTask.FromResult<Snapshot<TAggregate>?>(new() { StreamId = streamId, State = state, Version = s.Version, Timestamp = s.Timestamp });
            }
        }
        return ValueTask.FromResult<Snapshot<TAggregate>?>(null);
    }

    public ValueTask<IReadOnlyList<SnapshotInfo>> GetSnapshotHistoryAsync(string streamId, CancellationToken ct = default)
    {
        if (_snapshots.TryGetValue(streamId, out var list))
        {
            lock (list) return ValueTask.FromResult<IReadOnlyList<SnapshotInfo>>(list.Select(s => new SnapshotInfo(s.Version, s.Timestamp)).ToList());
        }
        return ValueTask.FromResult<IReadOnlyList<SnapshotInfo>>([]);
    }

    public ValueTask DeleteAsync(string streamId, CancellationToken ct = default) { _snapshots.TryRemove(streamId, out _); return ValueTask.CompletedTask; }

    public ValueTask DeleteBeforeVersionAsync(string streamId, long version, CancellationToken ct = default)
    {
        if (_snapshots.TryGetValue(streamId, out var list)) lock (list) list.RemoveAll(s => s.Version < version);
        return ValueTask.CompletedTask;
    }

    public ValueTask CleanupAsync(string streamId, int keepCount, CancellationToken ct = default)
    {
        if (_snapshots.TryGetValue(streamId, out var list)) lock (list) if (list.Count > keepCount) list.RemoveRange(0, list.Count - keepCount);
        return ValueTask.CompletedTask;
    }
}
