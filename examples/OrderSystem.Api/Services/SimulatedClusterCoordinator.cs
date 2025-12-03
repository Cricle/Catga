using Catga.Cluster;

namespace OrderSystem.Api.Services;

/// <summary>
/// Simulated cluster coordinator for single-node demo.
/// In production, use real DotNext Raft cluster.
/// </summary>
public sealed class SimulatedClusterCoordinator : IClusterCoordinator
{
    private readonly string _nodeId;
    private bool _isLeader = true;

    public SimulatedClusterCoordinator(string? nodeId = null)
    {
        _nodeId = nodeId ?? $"node-{Environment.MachineName}-{Guid.NewGuid():N}"[..16];
    }

    public bool IsLeader => _isLeader;
    public string? LeaderEndpoint => _isLeader ? "localhost:5000" : null;
    public string NodeId => _nodeId;

    public event Action<bool>? LeadershipChanged;

    /// <summary>
    /// Simulate leadership change (for demo purposes).
    /// </summary>
    public void SimulateLeadershipChange(bool isLeader)
    {
        _isLeader = isLeader;
        LeadershipChanged?.Invoke(isLeader);
    }

    public Task<bool> WaitForLeadershipAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        return Task.FromResult(_isLeader);
    }

    public async Task<bool> ExecuteIfLeaderAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        if (!_isLeader) return false;
        await action(ct);
        return true;
    }

    public async Task<(bool IsLeader, T? Result)> ExecuteIfLeaderAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        if (!_isLeader) return (false, default);
        var result = await action(ct);
        return (true, result);
    }
}
