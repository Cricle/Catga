namespace Catga.Abstractions;

/// <summary>
/// Leader election abstraction for cluster coordination.
/// Used for singleton tasks, background job scheduling, etc.
/// </summary>
public interface ILeaderElection
{
    /// <summary>Try to become leader for a given election.</summary>
    /// <param name="electionId">Unique election identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Leadership handle if acquired, null otherwise</returns>
    ValueTask<ILeadershipHandle?> TryAcquireLeadershipAsync(
        string electionId,
        CancellationToken ct = default);

    /// <summary>Wait to become leader.</summary>
    /// <param name="electionId">Unique election identifier</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Leadership handle when acquired</returns>
    ValueTask<ILeadershipHandle> AcquireLeadershipAsync(
        string electionId,
        TimeSpan timeout,
        CancellationToken ct = default);

    /// <summary>Check if current node is leader.</summary>
    ValueTask<bool> IsLeaderAsync(string electionId, CancellationToken ct = default);

    /// <summary>Get current leader information.</summary>
    ValueTask<LeaderInfo?> GetLeaderAsync(string electionId, CancellationToken ct = default);

    /// <summary>Watch for leadership changes.</summary>
    IAsyncEnumerable<LeadershipChange> WatchAsync(string electionId, CancellationToken ct = default);
}

/// <summary>Leadership handle - dispose to resign.</summary>
public interface ILeadershipHandle : IAsyncDisposable
{
    /// <summary>Election identifier.</summary>
    string ElectionId { get; }

    /// <summary>This node's identifier.</summary>
    string NodeId { get; }

    /// <summary>Whether leadership is still held.</summary>
    bool IsLeader { get; }

    /// <summary>When leadership was acquired.</summary>
    DateTimeOffset AcquiredAt { get; }

    /// <summary>Extend leadership lease.</summary>
    ValueTask ExtendAsync(CancellationToken ct = default);

    /// <summary>Event raised when leadership is lost.</summary>
    event Action? OnLeadershipLost;
}

/// <summary>Leader information.</summary>
public readonly record struct LeaderInfo
{
    /// <summary>Leader node identifier.</summary>
    public string NodeId { get; init; }

    /// <summary>When leadership was acquired.</summary>
    public DateTimeOffset AcquiredAt { get; init; }

    /// <summary>Leader's endpoint (if available).</summary>
    public string? Endpoint { get; init; }
}

/// <summary>Leadership change event.</summary>
public readonly record struct LeadershipChange
{
    /// <summary>Type of change.</summary>
    public LeadershipChangeType Type { get; init; }

    /// <summary>Previous leader (if any).</summary>
    public LeaderInfo? PreviousLeader { get; init; }

    /// <summary>New leader (if any).</summary>
    public LeaderInfo? NewLeader { get; init; }

    /// <summary>When the change occurred.</summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>Leadership change types.</summary>
public enum LeadershipChangeType
{
    /// <summary>New leader elected.</summary>
    Elected,

    /// <summary>Leader resigned.</summary>
    Resigned,

    /// <summary>Leader lost (timeout/failure).</summary>
    Lost
}

/// <summary>Leader election options.</summary>
public sealed class LeaderElectionOptions
{
    /// <summary>This node's unique identifier.</summary>
    public string NodeId { get; set; } = Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Leadership lease duration.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Lease renewal interval (should be less than lease duration).</summary>
    public TimeSpan RenewInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Key prefix for storage.</summary>
    public string KeyPrefix { get; set; } = "catga:leader:";

    /// <summary>This node's endpoint (optional, for discovery).</summary>
    public string? Endpoint { get; set; }
}
