using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.EventSourcing;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>
/// In-memory enhanced snapshot store with version history support.
/// </summary>
public class InMemoryEnhancedSnapshotStore : IEnhancedSnapshotStore
{
    private readonly IMessageSerializer _serializer;
    private readonly ConcurrentDictionary<string, SortedList<long, SnapshotEntry>> _snapshots = new();

    public InMemoryEnhancedSnapshotStore(IMessageSerializer serializer)
    {
        _serializer = serializer;
    }

    public ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        CancellationToken ct = default) where TAggregate : class
    {
        if (!_snapshots.TryGetValue(streamId, out var versions) || versions.Count == 0)
            return new ValueTask<Snapshot<TAggregate>?>((Snapshot<TAggregate>?)null);

        var latest = versions.Values[^1];
        var state = _serializer.Deserialize<TAggregate>(latest.Data);
        return new ValueTask<Snapshot<TAggregate>?>(new Snapshot<TAggregate>
        {
            StreamId = streamId,
            State = state!,
            Version = latest.Version,
            Timestamp = latest.Timestamp
        });
    }

    public ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        TAggregate state,
        long version,
        CancellationToken ct = default) where TAggregate : class
    {
        var data = _serializer.Serialize(state);
        var entry = new SnapshotEntry(version, DateTime.UtcNow, data);

        var versions = _snapshots.GetOrAdd(streamId, _ => new SortedList<long, SnapshotEntry>());
        lock (versions)
        {
            versions[version] = entry;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
    {
        _snapshots.TryRemove(streamId, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<Snapshot<TAggregate>?> LoadAtVersionAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        long version,
        CancellationToken ct = default) where TAggregate : class
    {
        if (!_snapshots.TryGetValue(streamId, out var versions) || versions.Count == 0)
            return new ValueTask<Snapshot<TAggregate>?>((Snapshot<TAggregate>?)null);

        lock (versions)
        {
            // Find the snapshot at or before the requested version
            SnapshotEntry? found = null;
            foreach (var kvp in versions)
            {
                if (kvp.Key <= version)
                    found = kvp.Value;
                else
                    break;
            }

            if (found == null)
                return new ValueTask<Snapshot<TAggregate>?>((Snapshot<TAggregate>?)null);

            var state = _serializer.Deserialize<TAggregate>(found.Data);
            return new ValueTask<Snapshot<TAggregate>?>(new Snapshot<TAggregate>
            {
                StreamId = streamId,
                State = state!,
                Version = found.Version,
                Timestamp = found.Timestamp
            });
        }
    }

    public ValueTask<IReadOnlyList<SnapshotInfo>> GetSnapshotHistoryAsync(
        string streamId,
        CancellationToken ct = default)
    {
        if (!_snapshots.TryGetValue(streamId, out var versions) || versions.Count == 0)
            return new ValueTask<IReadOnlyList<SnapshotInfo>>(Array.Empty<SnapshotInfo>());

        lock (versions)
        {
            var history = versions.Values
                .Select(e => new SnapshotInfo(e.Version, e.Timestamp))
                .ToList();
            return new ValueTask<IReadOnlyList<SnapshotInfo>>(history);
        }
    }

    public ValueTask DeleteBeforeVersionAsync(
        string streamId,
        long version,
        CancellationToken ct = default)
    {
        if (!_snapshots.TryGetValue(streamId, out var versions))
            return ValueTask.CompletedTask;

        lock (versions)
        {
            var keysToRemove = versions.Keys.Where(k => k < version).ToList();
            foreach (var key in keysToRemove)
            {
                versions.Remove(key);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CleanupAsync(
        string streamId,
        int keepCount,
        CancellationToken ct = default)
    {
        if (!_snapshots.TryGetValue(streamId, out var versions))
            return ValueTask.CompletedTask;

        lock (versions)
        {
            while (versions.Count > keepCount)
            {
                versions.RemoveAt(0); // Remove oldest
            }
        }

        return ValueTask.CompletedTask;
    }

    private sealed record SnapshotEntry(long Version, DateTime Timestamp, byte[] Data);
}
