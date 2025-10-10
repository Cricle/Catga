using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster.DotNext;

/// <summary>
/// Catga wrapper for DotNext Raft cluster - adapts IRaftCluster to ICatgaRaftCluster
/// </summary>
public sealed class CatgaRaftCluster : ICatgaRaftCluster, IDisposable
{
    private readonly IRaftCluster _raftCluster;
    private readonly ILogger<CatgaRaftCluster> _logger;

    public CatgaRaftCluster(IRaftCluster raftCluster, ILogger<CatgaRaftCluster> logger)
    {
        _raftCluster = raftCluster ?? throw new ArgumentNullException(nameof(raftCluster));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Current leader member ID (null if no leader elected)
    /// </summary>
    public string? LeaderId
    {
        get
        {
            var leader = _raftCluster.Leader;
            if (leader == null)
            {
                _logger.LogDebug("No leader elected yet");
                return null;
            }

            // Convert ClusterMemberId to string
            return leader.Id.ToString();
        }
    }

    /// <summary>
    /// Current node's member ID
    /// </summary>
    public string LocalMemberId
    {
        get
        {
            var localMember = _raftCluster.Members.FirstOrDefault(m => !m.IsRemote);
            if (localMember == null)
            {
                _logger.LogWarning("Cannot find local member in cluster");
                return "unknown";
            }

            return localMember.Id.ToString();
        }
    }

    /// <summary>
    /// Is current node the leader?
    /// </summary>
    public bool IsLeader
    {
        get
        {
            var leader = _raftCluster.Leader;
            if (leader == null)
            {
                return false;
            }

            // Check if leader is local (not remote)
            return !leader.IsRemote;
        }
    }

    /// <summary>
    /// All cluster members
    /// </summary>
    public IReadOnlyList<ClusterMember> Members
    {
        get
        {
            var leader = _raftCluster.Leader;
            var leaderId = leader?.Id.ToString();

            return _raftCluster.Members
                .Select(m => new ClusterMember
                {
                    Id = m.Id.ToString(),
                    Endpoint = m.EndPoint is System.Net.IPEndPoint ipEndPoint
                        ? new Uri($"http://{ipEndPoint.Address}:{ipEndPoint.Port}")
                        : new Uri($"http://unknown"),
                    Status = MapMemberStatus(m),
                    IsLeader = m.Id.ToString() == leaderId
                })
                .ToList();
        }
    }

    /// <summary>
    /// Current term (election term)
    /// </summary>
    public long Term => _raftCluster.Term;

    /// <summary>
    /// Cluster readiness status
    /// </summary>
    public ClusterStatus Status
    {
        get
        {
            // Check if we have a leader
            if (_raftCluster.Leader == null)
            {
                return ClusterStatus.NotReady;
            }

            // Check cluster consensus
            var consensusSize = _raftCluster.Members.Count(m => 
                m.Status == global::DotNext.Net.Cluster.ClusterMemberStatus.Available);
            var totalSize = _raftCluster.Members.Count();

            // Need majority for healthy cluster
            var majoritySize = (totalSize / 2) + 1;
            
            if (consensusSize >= majoritySize)
            {
                return ClusterStatus.Ready;
            }
            else if (consensusSize > 0)
            {
                return ClusterStatus.Degraded;
            }
            else
            {
                return ClusterStatus.NotReady;
            }
        }
    }

    /// <summary>
    /// Map DotNext ClusterMemberStatus to Catga ClusterMemberStatus
    /// </summary>
    private static ClusterMemberStatus MapMemberStatus(IClusterMember member)
    {
        return member.Status switch
        {
            global::DotNext.Net.Cluster.ClusterMemberStatus.Available => ClusterMemberStatus.Available,
            global::DotNext.Net.Cluster.ClusterMemberStatus.Unavailable => ClusterMemberStatus.Unavailable,
            _ => ClusterMemberStatus.Unknown
        };
    }

    public void Dispose()
    {
        // IRaftCluster is managed by DI container, don't dispose here
        _logger.LogDebug("CatgaRaftCluster disposed");
    }
}

