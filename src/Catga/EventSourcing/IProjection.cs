using Catga.Abstractions;

namespace Catga.EventSourcing;

/// <summary>
/// Base interface for event projections.
/// Projections transform event streams into read models.
/// </summary>
public interface IProjection
{
    /// <summary>Unique name of the projection.</summary>
    string Name { get; }

    /// <summary>Apply an event to update the projection state.</summary>
    ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default);

    /// <summary>Reset the projection state (for rebuilding).</summary>
    ValueTask ResetAsync(CancellationToken ct = default);
}

/// <summary>
/// Checkpoint for tracking projection progress.
/// </summary>
public sealed class ProjectionCheckpoint
{
    /// <summary>Name of the projection.</summary>
    public required string ProjectionName { get; init; }

    /// <summary>Last processed event position (global or stream-specific).</summary>
    public long Position { get; set; }

    /// <summary>When the checkpoint was last updated.</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>Optional stream ID if tracking per-stream.</summary>
    public string? StreamId { get; init; }
}

/// <summary>
/// Store for projection checkpoints.
/// </summary>
public interface IProjectionCheckpointStore
{
    /// <summary>Save or update a checkpoint.</summary>
    ValueTask SaveAsync(ProjectionCheckpoint checkpoint, CancellationToken ct = default);

    /// <summary>Load checkpoint for a projection.</summary>
    ValueTask<ProjectionCheckpoint?> LoadAsync(string projectionName, CancellationToken ct = default);

    /// <summary>Delete checkpoint (for rebuild).</summary>
    ValueTask DeleteAsync(string projectionName, CancellationToken ct = default);
}

/// <summary>
/// Runs catch-up projection from event store.
/// </summary>
public sealed class CatchUpProjectionRunner<TProjection> where TProjection : IProjection
{
    private readonly IEventStore _eventStore;
    private readonly IProjectionCheckpointStore _checkpointStore;
    private readonly TProjection _projection;
    private readonly string _projectionName;

    public CatchUpProjectionRunner(
        IEventStore eventStore,
        IProjectionCheckpointStore checkpointStore,
        TProjection projection,
        string projectionName)
    {
        _eventStore = eventStore;
        _checkpointStore = checkpointStore;
        _projection = projection;
        _projectionName = projectionName;
    }

    /// <summary>Run the projection, resuming from last checkpoint.</summary>
    public async ValueTask RunAsync(CancellationToken ct = default)
    {
        var checkpoint = await _checkpointStore.LoadAsync(_projectionName, ct);
        var lastProcessedPosition = checkpoint?.Position ?? -1;

        // Read all streams and apply events
        var streams = await _eventStore.GetAllStreamIdsAsync(ct);
        long processedCount = 0;

        foreach (var streamId in streams)
        {
            // Read from version 0 to get all events, then filter by position
            var eventStream = await _eventStore.ReadAsync(streamId, 0, cancellationToken: ct);
            foreach (var stored in eventStream.Events)
            {
                // Apply events that haven't been processed yet
                if (stored.Version > lastProcessedPosition)
                {
                    await _projection.ApplyAsync(stored.Event, ct);
                    processedCount++;
                }
            }
        }

        // Update checkpoint
        if (processedCount > 0 || checkpoint == null)
        {
            await _checkpointStore.SaveAsync(new ProjectionCheckpoint
            {
                ProjectionName = _projectionName,
                Position = lastProcessedPosition + processedCount,
                LastUpdated = DateTime.UtcNow
            }, ct);
        }
    }
}

/// <summary>
/// Live projection handler for real-time event processing.
/// </summary>
public sealed class LiveProjection<TProjection> where TProjection : IProjection
{
    private readonly TProjection _projection;

    public LiveProjection(TProjection projection)
    {
        _projection = projection;
    }

    /// <summary>Handle a new event in real-time.</summary>
    public ValueTask HandleAsync(IEvent @event, CancellationToken ct = default)
        => _projection.ApplyAsync(@event, ct);
}

/// <summary>
/// Rebuilds a projection from scratch.
/// </summary>
public sealed class ProjectionRebuilder<TProjection> where TProjection : IProjection
{
    private readonly IEventStore _eventStore;
    private readonly IProjectionCheckpointStore _checkpointStore;
    private readonly TProjection _projection;
    private readonly string _projectionName;

    public ProjectionRebuilder(
        IEventStore eventStore,
        IProjectionCheckpointStore checkpointStore,
        TProjection projection,
        string projectionName)
    {
        _eventStore = eventStore;
        _checkpointStore = checkpointStore;
        _projection = projection;
        _projectionName = projectionName;
    }

    /// <summary>Rebuild the projection from scratch.</summary>
    public async ValueTask RebuildAsync(CancellationToken ct = default)
    {
        // Reset projection state
        await _projection.ResetAsync(ct);

        // Delete existing checkpoint
        await _checkpointStore.DeleteAsync(_projectionName, ct);

        // Replay all events
        var streams = await _eventStore.GetAllStreamIdsAsync(ct);
        long processedCount = 0;

        foreach (var streamId in streams)
        {
            var eventStream = await _eventStore.ReadAsync(streamId, 0, cancellationToken: ct);
            foreach (var stored in eventStream.Events)
            {
                await _projection.ApplyAsync(stored.Event, ct);
                processedCount++;
            }
        }

        // Save new checkpoint
        await _checkpointStore.SaveAsync(new ProjectionCheckpoint
        {
            ProjectionName = _projectionName,
            Position = processedCount,
            LastUpdated = DateTime.UtcNow
        }, ct);
    }
}
