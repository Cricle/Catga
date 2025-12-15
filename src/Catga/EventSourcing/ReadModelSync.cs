using Catga.Abstractions;

namespace Catga.EventSourcing;

/// <summary>
/// Interface for read model synchronization.
/// Follows Open-Closed principle - extend with new strategies without modifying existing code.
/// </summary>
public interface IReadModelSynchronizer
{
    /// <summary>Synchronize read model with event store</summary>
    ValueTask SyncAsync(CancellationToken ct = default);

    /// <summary>Get the last synchronization time</summary>
    ValueTask<DateTime?> GetLastSyncTimeAsync(CancellationToken ct = default);
}

/// <summary>
/// Interface for synchronization strategy.
/// Implements Strategy pattern for different sync approaches.
/// </summary>
public interface ISyncStrategy
{
    /// <summary>Strategy name</summary>
    string Name { get; }

    /// <summary>Execute synchronization according to strategy</summary>
    ValueTask ExecuteAsync(IEnumerable<ChangeRecord> changes, CancellationToken ct = default);
}

/// <summary>
/// Interface for tracking changes to be synchronized.
/// </summary>
public interface IChangeTracker
{
    /// <summary>Track a change for later synchronization</summary>
    void TrackChange(ChangeRecord change);

    /// <summary>Get all pending changes not yet synchronized</summary>
    ValueTask<IReadOnlyList<ChangeRecord>> GetPendingChangesAsync(CancellationToken ct = default);

    /// <summary>Mark changes as synchronized</summary>
    ValueTask MarkAsSyncedAsync(IEnumerable<string> changeIds, CancellationToken ct = default);
}

/// <summary>
/// Generic interface for read model storage.
/// </summary>
public interface IReadModelStore<TReadModel> where TReadModel : class
{
    /// <summary>Get read model by ID</summary>
    ValueTask<TReadModel?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Save or update read model</summary>
    ValueTask SaveAsync(string id, TReadModel model, CancellationToken ct = default);

    /// <summary>Delete read model</summary>
    ValueTask DeleteAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// Record representing a change to be synchronized.
/// </summary>
public sealed record ChangeRecord
{
    public required string Id { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required ChangeType Type { get; init; }
    public required IEvent Event { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsSynced { get; set; }
}

/// <summary>
/// Type of change tracked.
/// </summary>
public enum ChangeType
{
    Created,
    Updated,
    Deleted
}

/// <summary>
/// Realtime synchronization strategy - sync immediately on each change.
/// </summary>
public sealed class RealtimeSyncStrategy : ISyncStrategy
{
    private readonly Func<ChangeRecord, ValueTask> _syncAction;

    public string Name => "Realtime";

    public RealtimeSyncStrategy(Func<ChangeRecord, ValueTask> syncAction)
    {
        _syncAction = syncAction;
    }

    public async ValueTask ExecuteAsync(IEnumerable<ChangeRecord> changes, CancellationToken ct = default)
    {
        foreach (var change in changes)
        {
            ct.ThrowIfCancellationRequested();
            await _syncAction(change);
        }
    }
}

/// <summary>
/// Batch synchronization strategy - accumulate changes and sync in batches.
/// </summary>
public sealed class BatchSyncStrategy : ISyncStrategy
{
    private readonly int _batchSize;
    private readonly Func<IReadOnlyList<ChangeRecord>, ValueTask> _syncAction;

    public string Name => "Batch";

    public BatchSyncStrategy(int batchSize, Func<IReadOnlyList<ChangeRecord>, ValueTask> syncAction)
    {
        _batchSize = batchSize;
        _syncAction = syncAction;
    }

    public async ValueTask ExecuteAsync(IEnumerable<ChangeRecord> changes, CancellationToken ct = default)
    {
        var batch = new List<ChangeRecord>(_batchSize);

        foreach (var change in changes)
        {
            ct.ThrowIfCancellationRequested();
            batch.Add(change);

            if (batch.Count >= _batchSize)
            {
                await _syncAction(batch);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _syncAction(batch);
        }
    }
}

/// <summary>
/// Scheduled synchronization strategy - sync at specified intervals.
/// </summary>
public sealed class ScheduledSyncStrategy : ISyncStrategy
{
    private readonly TimeSpan _interval;
    private readonly Func<IReadOnlyList<ChangeRecord>, ValueTask> _syncAction;
    private DateTime _lastSync = DateTime.MinValue;

    public string Name => "Scheduled";

    public ScheduledSyncStrategy(TimeSpan interval, Func<IReadOnlyList<ChangeRecord>, ValueTask> syncAction)
    {
        _interval = interval;
        _syncAction = syncAction;
    }

    public async ValueTask ExecuteAsync(IEnumerable<ChangeRecord> changes, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (now - _lastSync < _interval)
        {
            return; // Not time yet
        }

        var changeList = changes.ToList();
        if (changeList.Count > 0)
        {
            await _syncAction(changeList);
            _lastSync = now;
        }
    }
}

/// <summary>
/// Default in-memory change tracker implementation.
/// </summary>
public sealed class InMemoryChangeTracker : IChangeTracker
{
    private readonly List<ChangeRecord> _changes = new();
    private readonly object _lock = new();

    public void TrackChange(ChangeRecord change)
    {
        lock (_lock)
        {
            _changes.Add(change);
        }
    }

    public ValueTask<IReadOnlyList<ChangeRecord>> GetPendingChangesAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var pending = _changes.Where(c => !c.IsSynced).ToList();
            return ValueTask.FromResult<IReadOnlyList<ChangeRecord>>(pending);
        }
    }

    public ValueTask MarkAsSyncedAsync(IEnumerable<string> changeIds, CancellationToken ct = default)
    {
        var idSet = changeIds.ToHashSet();
        lock (_lock)
        {
            foreach (var change in _changes.Where(c => idSet.Contains(c.Id)))
            {
                change.IsSynced = true;
            }
        }
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Default read model synchronizer implementation.
/// </summary>
public sealed class DefaultReadModelSynchronizer : IReadModelSynchronizer
{
    private readonly IChangeTracker _changeTracker;
    private readonly ISyncStrategy _strategy;
    private DateTime? _lastSyncTime;

    public DefaultReadModelSynchronizer(IChangeTracker changeTracker, ISyncStrategy strategy)
    {
        _changeTracker = changeTracker;
        _strategy = strategy;
    }

    public async ValueTask SyncAsync(CancellationToken ct = default)
    {
        var pending = await _changeTracker.GetPendingChangesAsync(ct);
        if (pending.Count == 0) return;

        await _strategy.ExecuteAsync(pending, ct);
        await _changeTracker.MarkAsSyncedAsync(pending.Select(c => c.Id), ct);
        _lastSyncTime = DateTime.UtcNow;
    }

    public ValueTask<DateTime?> GetLastSyncTimeAsync(CancellationToken ct = default)
    {
        return ValueTask.FromResult(_lastSyncTime);
    }
}
