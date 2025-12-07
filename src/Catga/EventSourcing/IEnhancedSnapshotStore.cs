using System.Diagnostics.CodeAnalysis;

namespace Catga.EventSourcing;

/// <summary>
/// Enhanced snapshot store interface with version-based queries and cleanup.
/// Extends ISnapshotStore with multi-version snapshot support.
/// </summary>
public interface IEnhancedSnapshotStore : ISnapshotStore
{
    /// <summary>
    /// Load snapshot at or before specific version.
    /// Useful for time travel to reconstruct state at a specific point.
    /// </summary>
    ValueTask<Snapshot<TAggregate>?> LoadAtVersionAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        long version,
        CancellationToken ct = default) where TAggregate : class;

    /// <summary>
    /// Get all snapshot metadata for a stream (without loading full state).
    /// </summary>
    ValueTask<IReadOnlyList<SnapshotInfo>> GetSnapshotHistoryAsync(
        string streamId,
        CancellationToken ct = default);

    /// <summary>
    /// Delete snapshots before a specific version.
    /// </summary>
    ValueTask DeleteBeforeVersionAsync(
        string streamId,
        long version,
        CancellationToken ct = default);

    /// <summary>
    /// Cleanup old snapshots, keeping only the most recent ones.
    /// </summary>
    ValueTask CleanupAsync(
        string streamId,
        int keepCount,
        CancellationToken ct = default);
}

/// <summary>
/// Snapshot metadata without full state.
/// </summary>
public sealed class SnapshotInfo
{
    public SnapshotInfo(long version, DateTime timestamp)
    {
        Version = version;
        Timestamp = timestamp;
    }

    public long Version { get; }
    public DateTime Timestamp { get; }
}

/// <summary>
/// Time-based snapshot strategy.
/// Takes snapshot after a specified time interval since last snapshot.
/// </summary>
public sealed class TimeBasedSnapshotStrategy
{
    private readonly TimeSpan _interval;

    public TimeBasedSnapshotStrategy(TimeSpan interval)
    {
        _interval = interval;
    }

    public bool ShouldTakeSnapshot(DateTime lastSnapshotTime)
        => DateTime.UtcNow - lastSnapshotTime >= _interval;
}

/// <summary>
/// Composite strategy combining event count and time-based strategies.
/// Takes snapshot when either threshold is met.
/// </summary>
public sealed class CompositeSnapshotStrategy
{
    private readonly EventCountSnapshotStrategy _eventStrategy;
    private readonly TimeBasedSnapshotStrategy _timeStrategy;

    public CompositeSnapshotStrategy(
        EventCountSnapshotStrategy eventStrategy,
        TimeBasedSnapshotStrategy timeStrategy)
    {
        _eventStrategy = eventStrategy;
        _timeStrategy = timeStrategy;
    }

    public bool ShouldTakeSnapshot(long currentVersion, long lastSnapshotVersion, DateTime lastSnapshotTime)
        => _eventStrategy.ShouldTakeSnapshot(currentVersion, lastSnapshotVersion)
           || _timeStrategy.ShouldTakeSnapshot(lastSnapshotTime);
}

/// <summary>
/// Auto snapshot manager that automatically takes snapshots based on strategy.
/// </summary>
public sealed class AutoSnapshotManager<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>
    where TAggregate : class, IAggregateRoot
{
    private readonly IEnhancedSnapshotStore _snapshotStore;
    private readonly ISnapshotStrategy _strategy;

    public AutoSnapshotManager(
        IEnhancedSnapshotStore snapshotStore,
        ISnapshotStrategy strategy)
    {
        _snapshotStore = snapshotStore;
        _strategy = strategy;
    }

    /// <summary>
    /// Check if snapshot should be taken and save if needed.
    /// </summary>
    public async ValueTask CheckAndSaveSnapshotAsync(
        string streamId,
        TAggregate aggregate,
        long currentVersion,
        CancellationToken ct = default)
    {
        var lastSnapshot = await _snapshotStore.LoadAsync<TAggregate>(streamId, ct);
        var lastVersion = lastSnapshot?.Version ?? 0;

        if (_strategy.ShouldTakeSnapshot(currentVersion, lastVersion))
        {
            await _snapshotStore.SaveAsync(streamId, aggregate, currentVersion, ct);
        }
    }
}
