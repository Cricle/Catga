using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.EventSourcing;

/// <summary>
/// Time travel service with snapshot optimization.
/// Uses snapshots to speed up state reconstruction for large event streams.
/// </summary>
public sealed class TimeTravelServiceWithSnapshots<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>
    : ITimeTravelService<TAggregate>
    where TAggregate : class, IAggregateRoot, new()
{
    private readonly IEventStore _eventStore;
    private readonly IEnhancedSnapshotStore _snapshotStore;

    public TimeTravelServiceWithSnapshots(IEventStore eventStore, IEnhancedSnapshotStore snapshotStore)
    {
        _eventStore = eventStore;
        _snapshotStore = snapshotStore;
    }

    /// <inheritdoc />
    public async ValueTask<TAggregate?> GetStateAtVersionAsync(
        string aggregateId,
        long version,
        CancellationToken ct = default)
    {
        var streamId = $"{typeof(TAggregate).Name}-{aggregateId}";

        // Try to load snapshot at or before target version
        var snapshot = await _snapshotStore.LoadAtVersionAsync<TAggregate>(streamId, version, ct);

        TAggregate aggregate;
        long fromVersion;

        if (snapshot.HasValue)
        {
            // Start from snapshot - clone to avoid modifying cached state
            aggregate = CloneAggregate(snapshot.Value.State);
            fromVersion = snapshot.Value.Version + 1;
        }
        else
        {
            // No snapshot, start from beginning
            aggregate = new TAggregate();
            fromVersion = 0;
        }

        // Read remaining events from snapshot version to target version
        var eventStream = await _eventStore.ReadToVersionAsync(streamId, version, ct);
        var eventsToApply = eventStream.Events
            .Where(e => e.Version >= fromVersion && e.Version <= version)
            .Select(e => e.Event);

        aggregate.LoadFromHistory(eventsToApply);
        return aggregate;
    }

    /// <inheritdoc />
    public async ValueTask<TAggregate?> GetStateAtTimestampAsync(
        string aggregateId,
        DateTime timestamp,
        CancellationToken ct = default)
    {
        var streamId = $"{typeof(TAggregate).Name}-{aggregateId}";
        var eventStream = await _eventStore.ReadToTimestampAsync(streamId, timestamp, ct);

        if (eventStream.Events.Count == 0)
            return null;

        // Find the version at the timestamp
        var targetVersion = eventStream.Version;

        // Use snapshot-optimized version lookup
        return await GetStateAtVersionAsync(aggregateId, targetVersion, ct);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistoryAsync(
        string aggregateId,
        CancellationToken ct = default)
    {
        var streamId = $"{typeof(TAggregate).Name}-{aggregateId}";
        return await _eventStore.GetVersionHistoryAsync(streamId, ct);
    }

    /// <inheritdoc />
    public async ValueTask<StateComparison<TAggregate>> CompareVersionsAsync(
        string aggregateId,
        long fromVersion,
        long toVersion,
        CancellationToken ct = default)
    {
        var fromState = await GetStateAtVersionAsync(aggregateId, fromVersion, ct);
        var toState = await GetStateAtVersionAsync(aggregateId, toVersion, ct);

        var streamId = $"{typeof(TAggregate).Name}-{aggregateId}";
        var history = await _eventStore.GetVersionHistoryAsync(streamId, ct);

        var eventsBetween = history
            .Where(v => v.Version > fromVersion && v.Version <= toVersion)
            .ToList();

        return new StateComparison<TAggregate>
        {
            FromVersion = fromVersion,
            ToVersion = toVersion,
            FromState = fromState,
            ToState = toState,
            EventsBetween = eventsBetween
        };
    }

    private static TAggregate CloneAggregate(TAggregate source)
    {
        var clone = new TAggregate();
        var type = typeof(TAggregate);
        foreach (var prop in type.GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            try
            {
                prop.SetValue(clone, prop.GetValue(source));
            }
            catch
            {
                // Skip properties that can't be set
            }
        }
        return clone;
    }
}
