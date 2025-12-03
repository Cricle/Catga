using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using Microsoft.Extensions.Logging;

namespace Catga.Cluster;

/// <summary>
/// Cluster coordinator implementation using DotNext Raft consensus.
/// Provides simple API for leader election and distributed coordination.
/// </summary>
public sealed class ClusterCoordinator : IClusterCoordinator, IDisposable
{
    private readonly IRaftCluster _cluster;
    private readonly ILogger<ClusterCoordinator>? _logger;
    private readonly string _nodeId;
    private bool _disposed;

    public ClusterCoordinator(IRaftCluster cluster, ILogger<ClusterCoordinator>? logger = null)
    {
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
        _logger = logger;
        _nodeId = Guid.NewGuid().ToString("N")[..8];

        _cluster.LeaderChanged += OnLeaderChanged;
    }

    /// <inheritdoc/>
    public bool IsLeader => _cluster.LeadershipToken.IsCancellationRequested == false
                            && _cluster.Leader?.IsRemote == false;

    /// <inheritdoc/>
    public string? LeaderEndpoint => _cluster.Leader?.EndPoint?.ToString();

    /// <inheritdoc/>
    public string NodeId => _nodeId;

    /// <inheritdoc/>
    public event Action<bool>? LeadershipChanged;

    /// <inheritdoc/>
    public async Task<bool> WaitForLeadershipAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (IsLeader)
                    return true;

                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> ExecuteIfLeaderAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        if (!IsLeader)
            return false;

        try
        {
            // Use leadership token to ensure we're still leader during execution
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cluster.LeadershipToken);
            await action(cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (_cluster.LeadershipToken.IsCancellationRequested)
        {
            _logger?.LogWarning("Lost leadership during execution");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<(bool IsLeader, T? Result)> ExecuteIfLeaderAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        if (!IsLeader)
            return (false, default);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cluster.LeadershipToken);
            var result = await action(cts.Token);
            return (true, result);
        }
        catch (OperationCanceledException) when (_cluster.LeadershipToken.IsCancellationRequested)
        {
            _logger?.LogWarning("Lost leadership during execution");
            return (false, default);
        }
    }

    private void OnLeaderChanged(ICluster cluster, IClusterMember? leader)
    {
        var isLeader = leader?.IsRemote == false;
        _logger?.LogInformation("Leadership changed: IsLeader={IsLeader}, Leader={Leader}",
            isLeader, leader?.EndPoint);
        LeadershipChanged?.Invoke(isLeader);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cluster.LeaderChanged -= OnLeaderChanged;
    }
}
