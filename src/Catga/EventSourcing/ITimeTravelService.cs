using System.Diagnostics.CodeAnalysis;

namespace Catga.EventSourcing;

/// <summary>
/// Service for time travel queries on event-sourced aggregates.
/// Allows reconstructing aggregate state at specific versions or timestamps.
/// </summary>
/// <typeparam name="TAggregate">The aggregate type.</typeparam>
public interface ITimeTravelService<TAggregate> where TAggregate : class, IAggregateRoot
{
    /// <summary>
    /// Get aggregate state at a specific version.
    /// </summary>
    /// <param name="id">Aggregate identifier.</param>
    /// <param name="version">Target version (0-based, inclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregate state at the specified version, or null if not found.</returns>
    ValueTask<TAggregate?> GetStateAtVersionAsync(
        string id,
        long version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get aggregate state at a specific point in time.
    /// </summary>
    /// <param name="id">Aggregate identifier.</param>
    /// <param name="timestamp">Target timestamp (UTC, inclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregate state at the specified timestamp, or null if not found.</returns>
    ValueTask<TAggregate?> GetStateAtTimestampAsync(
        string id,
        DateTime timestamp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get version history for an aggregate.
    /// </summary>
    /// <param name="id">Aggregate identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of version information.</returns>
    ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistoryAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare aggregate state between two versions.
    /// </summary>
    /// <param name="id">Aggregate identifier.</param>
    /// <param name="fromVersion">Starting version.</param>
    /// <param name="toVersion">Ending version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>State comparison result.</returns>
    ValueTask<StateComparison<TAggregate>> CompareVersionsAsync(
        string id,
        long fromVersion,
        long toVersion,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of comparing aggregate state between two versions.
/// </summary>
/// <typeparam name="TAggregate">The aggregate type.</typeparam>
public sealed class StateComparison<TAggregate> where TAggregate : class, IAggregateRoot
{
    /// <summary>Aggregate state at the starting version.</summary>
    public TAggregate? FromState { get; init; }

    /// <summary>Aggregate state at the ending version.</summary>
    public TAggregate? ToState { get; init; }

    /// <summary>Starting version.</summary>
    public long FromVersion { get; init; }

    /// <summary>Ending version.</summary>
    public long ToVersion { get; init; }

    /// <summary>Events between the two versions.</summary>
    public IReadOnlyList<VersionInfo> EventsBetween { get; init; } = [];
}

/// <summary>
/// Default implementation of time travel service.
/// </summary>
public sealed class TimeTravelService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>
    : ITimeTravelService<TAggregate>
    where TAggregate : class, IAggregateRoot, new()
{
    private readonly IEventStore _eventStore;

    public TimeTravelService(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async ValueTask<TAggregate?> GetStateAtVersionAsync(
        string id,
        long version,
        CancellationToken cancellationToken = default)
    {
        var streamId = GetStreamId(id);
        var stream = await _eventStore.ReadToVersionAsync(streamId, version, cancellationToken);

        if (stream.Events.Count == 0)
            return null;

        var aggregate = new TAggregate();
        aggregate.LoadFromHistory(stream.Events.Select(e => e.Event));
        return aggregate;
    }

    public async ValueTask<TAggregate?> GetStateAtTimestampAsync(
        string id,
        DateTime timestamp,
        CancellationToken cancellationToken = default)
    {
        var streamId = GetStreamId(id);
        var stream = await _eventStore.ReadToTimestampAsync(streamId, timestamp, cancellationToken);

        if (stream.Events.Count == 0)
            return null;

        var aggregate = new TAggregate();
        aggregate.LoadFromHistory(stream.Events.Select(e => e.Event));
        return aggregate;
    }

    public async ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistoryAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var streamId = GetStreamId(id);
        return await _eventStore.GetVersionHistoryAsync(streamId, cancellationToken);
    }

    public async ValueTask<StateComparison<TAggregate>> CompareVersionsAsync(
        string id,
        long fromVersion,
        long toVersion,
        CancellationToken cancellationToken = default)
    {
        var streamId = GetStreamId(id);

        // Get state at fromVersion
        var fromStream = await _eventStore.ReadToVersionAsync(streamId, fromVersion, cancellationToken);
        TAggregate? fromState = null;
        if (fromStream.Events.Count > 0)
        {
            fromState = new TAggregate();
            fromState.LoadFromHistory(fromStream.Events.Select(e => e.Event));
        }

        // Get state at toVersion
        var toStream = await _eventStore.ReadToVersionAsync(streamId, toVersion, cancellationToken);
        TAggregate? toState = null;
        if (toStream.Events.Count > 0)
        {
            toState = new TAggregate();
            toState.LoadFromHistory(toStream.Events.Select(e => e.Event));
        }

        // Get events between versions as VersionInfo
        var eventsBetween = toStream.Events
            .Where(e => e.Version > fromVersion && e.Version <= toVersion)
            .Select(e => new VersionInfo { Version = e.Version, EventType = e.Event.GetType().Name, Timestamp = e.Timestamp })
            .ToList();

        return new StateComparison<TAggregate>
        {
            FromState = fromState,
            ToState = toState,
            FromVersion = fromVersion,
            ToVersion = toVersion,
            EventsBetween = eventsBetween
        };
    }

    private static string GetStreamId(string id) => $"{typeof(TAggregate).Name}-{id}";
}
