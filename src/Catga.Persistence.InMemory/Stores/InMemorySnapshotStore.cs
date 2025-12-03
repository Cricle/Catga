using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.EventSourcing;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>
/// In-memory snapshot store for development and testing.
/// Thread-safe, zero-allocation design.
/// </summary>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly ConcurrentDictionary<string, SnapshotEntry> _snapshots = new();

    public ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        TAggregate aggregate,
        long version,
        CancellationToken ct = default) where TAggregate : class
    {
        _snapshots[streamId] = new SnapshotEntry(aggregate, version, DateTime.UtcNow);
        return ValueTask.CompletedTask;
    }

    public ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        CancellationToken ct = default) where TAggregate : class
    {
        if (_snapshots.TryGetValue(streamId, out var entry) && entry.Aggregate is TAggregate state)
        {
            return ValueTask.FromResult<Snapshot<TAggregate>?>(new Snapshot<TAggregate>
            {
                StreamId = streamId,
                State = state,
                Version = entry.Version,
                Timestamp = entry.CreatedAt
            });
        }
        return ValueTask.FromResult<Snapshot<TAggregate>?>(null);
    }

    public ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
    {
        _snapshots.TryRemove(streamId, out _);
        return ValueTask.CompletedTask;
    }

    private readonly record struct SnapshotEntry(object Aggregate, long Version, DateTime CreatedAt);
}
