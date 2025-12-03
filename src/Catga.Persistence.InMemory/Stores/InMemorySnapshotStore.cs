using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.EventSourcing;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>In-memory snapshot store for development/testing.</summary>
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
