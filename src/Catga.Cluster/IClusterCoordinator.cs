namespace Catga.Cluster;

/// <summary>
/// Cluster coordinator abstraction for distributed coordination.
/// Wraps DotNext.Net.Cluster for Raft consensus.
/// </summary>
public interface IClusterCoordinator
{
    /// <summary>
    /// Whether this node is the current leader.
    /// </summary>
    bool IsLeader { get; }

    /// <summary>
    /// Current leader endpoint (null if no leader elected).
    /// </summary>
    string? LeaderEndpoint { get; }

    /// <summary>
    /// This node's unique identifier.
    /// </summary>
    string NodeId { get; }

    /// <summary>
    /// Wait until this node becomes leader or timeout.
    /// </summary>
    Task<bool> WaitForLeadershipAsync(TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// Execute action only if this node is leader.
    /// Returns false if not leader.
    /// </summary>
    Task<bool> ExecuteIfLeaderAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);

    /// <summary>
    /// Execute action only if this node is leader.
    /// Returns default(T) if not leader.
    /// </summary>
    Task<(bool IsLeader, T? Result)> ExecuteIfLeaderAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default);

    /// <summary>
    /// Event fired when leadership changes.
    /// </summary>
    event Action<bool>? LeadershipChanged;
}
