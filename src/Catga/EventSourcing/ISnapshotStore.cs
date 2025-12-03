using System.Diagnostics.CodeAnalysis;

namespace Catga.EventSourcing;

/// <summary>
/// Snapshot store for aggregate state persistence.
/// AOT-compatible, zero-allocation design.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>Save aggregate snapshot.</summary>
    ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        TAggregate aggregate,
        long version,
        CancellationToken ct = default) where TAggregate : class;

    /// <summary>Load latest snapshot.</summary>
    ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        CancellationToken ct = default) where TAggregate : class;

    /// <summary>Delete snapshot.</summary>
    ValueTask DeleteAsync(string streamId, CancellationToken ct = default);
}

/// <summary>Snapshot with metadata.</summary>
public readonly record struct Snapshot<TAggregate> where TAggregate : class
{
    /// <summary>Stream identifier.</summary>
    public required string StreamId { get; init; }

    /// <summary>Aggregate state.</summary>
    public required TAggregate State { get; init; }

    /// <summary>Version at snapshot time.</summary>
    public required long Version { get; init; }

    /// <summary>When snapshot was taken.</summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>Snapshot strategy for determining when to take snapshots.</summary>
public interface ISnapshotStrategy
{
    /// <summary>Check if snapshot should be taken.</summary>
    bool ShouldTakeSnapshot(long currentVersion, long lastSnapshotVersion);
}

/// <summary>Take snapshot every N events.</summary>
public sealed class EventCountSnapshotStrategy : ISnapshotStrategy
{
    private readonly int _eventThreshold;

    public EventCountSnapshotStrategy(int eventThreshold = 100)
    {
        _eventThreshold = eventThreshold;
    }

    public bool ShouldTakeSnapshot(long currentVersion, long lastSnapshotVersion)
        => currentVersion - lastSnapshotVersion >= _eventThreshold;
}

/// <summary>Snapshot options.</summary>
public sealed class SnapshotOptions
{
    /// <summary>Event count threshold for automatic snapshots.</summary>
    public int EventThreshold { get; set; } = 100;

    /// <summary>Enable automatic snapshot on threshold.</summary>
    public bool AutoSnapshot { get; set; } = true;

    /// <summary>Key prefix for snapshots.</summary>
    public string KeyPrefix { get; set; } = "catga:snapshot:";
}
