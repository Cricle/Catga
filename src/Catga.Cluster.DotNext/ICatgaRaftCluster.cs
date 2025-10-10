namespace Catga.Cluster.DotNext;

/// <summary>
/// Catga wrapper for Raft cluster - provides simplified interface
/// </summary>
public interface ICatgaRaftCluster
{
    /// <summary>
    /// Current leader member ID (null if no leader elected)
    /// </summary>
    string? LeaderId { get; }

    /// <summary>
    /// Current node's member ID
    /// </summary>
    string LocalMemberId { get; }

    /// <summary>
    /// Is current node the leader?
    /// </summary>
    bool IsLeader { get; }

    /// <summary>
    /// All cluster members
    /// </summary>
    IReadOnlyList<ClusterMember> Members { get; }

    /// <summary>
    /// Current term (election term)
    /// </summary>
    long Term { get; }

    /// <summary>
    /// Cluster readiness status
    /// </summary>
    ClusterStatus Status { get; }
}

/// <summary>
/// Cluster member information
/// </summary>
public class ClusterMember
{
    public required string Id { get; init; }
    public required Uri Endpoint { get; init; }
    public ClusterMemberStatus Status { get; init; }
    public bool IsLeader { get; init; }
}

/// <summary>
/// Cluster member status
/// </summary>
public enum ClusterMemberStatus
{
    Unknown = 0,
    Available = 1,
    Unavailable = 2
}

/// <summary>
/// Overall cluster status
/// </summary>
public enum ClusterStatus
{
    NotReady = 0,
    Ready = 1,
    Degraded = 2
}

