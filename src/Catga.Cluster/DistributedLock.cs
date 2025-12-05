using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Threading;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster;

/// <summary>
/// Distributed lock using Raft consensus.
/// Only the leader can acquire locks, ensuring cluster-wide consistency.
/// </summary>
public sealed class DistributedLock : IAsyncDisposable
{
    private readonly IClusterCoordinator _coordinator;
    private readonly AsyncExclusiveLock _localLock;
    private readonly ILogger? _logger;
    private readonly string _resource;
    private bool _acquired;

    private DistributedLock(IClusterCoordinator coordinator, string resource, ILogger? logger)
    {
        _coordinator = coordinator;
        _resource = resource;
        _logger = logger;
        _localLock = new AsyncExclusiveLock();
    }

    /// <summary>
    /// Try to acquire a distributed lock.
    /// Returns null if this node is not leader or lock acquisition failed.
    /// </summary>
    public static async Task<DistributedLock?> TryAcquireAsync(
        IClusterCoordinator coordinator,
        string resource,
        TimeSpan timeout,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        if (!coordinator.IsLeader)
        {
            logger?.LogDebug("Cannot acquire lock {Resource}: not leader", resource);
            return null;
        }

        var lockInstance = new DistributedLock(coordinator, resource, logger);

        try
        {
            if (await lockInstance._localLock.TryAcquireAsync(timeout, ct))
            {
                lockInstance._acquired = true;
                logger?.LogDebug("Acquired distributed lock {Resource}", resource);
                return lockInstance;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to acquire lock {Resource}", resource);
        }

        return null;
    }

    /// <summary>
    /// Whether the lock is still held and this node is still leader.
    /// </summary>
    public bool IsValid => _acquired && _coordinator.IsLeader;

    public ValueTask DisposeAsync()
    {
        if (_acquired)
        {
            _acquired = false;
            _localLock.Release();
            _logger?.LogDebug("Released distributed lock {Resource}", _resource);
        }

        _localLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
