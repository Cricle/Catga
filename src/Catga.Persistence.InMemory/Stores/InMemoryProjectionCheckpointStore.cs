using System.Collections.Concurrent;
using Catga.EventSourcing;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>
/// In-memory projection checkpoint store for development/testing.
/// </summary>
public sealed class InMemoryProjectionCheckpointStore : IProjectionCheckpointStore
{
    private readonly ConcurrentDictionary<string, ProjectionCheckpoint> _checkpoints = new();

    public ValueTask SaveAsync(ProjectionCheckpoint checkpoint, CancellationToken ct = default)
    {
        _checkpoints[checkpoint.ProjectionName] = checkpoint;
        return ValueTask.CompletedTask;
    }

    public ValueTask<ProjectionCheckpoint?> LoadAsync(string projectionName, CancellationToken ct = default)
    {
        return ValueTask.FromResult(
            _checkpoints.TryGetValue(projectionName, out var checkpoint) ? checkpoint : null);
    }

    public ValueTask DeleteAsync(string projectionName, CancellationToken ct = default)
    {
        _checkpoints.TryRemove(projectionName, out _);
        return ValueTask.CompletedTask;
    }

    public void Clear() => _checkpoints.Clear();
}
