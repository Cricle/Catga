using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Catga.Abstractions;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>In-memory leader election for development/testing.</summary>
public sealed class InMemoryLeaderElection(string? nodeId = null, TimeSpan? leaseDuration = null) : ILeaderElection
{
    private readonly ConcurrentDictionary<string, (string Node, DateTime Expires)> _leaders = new();
    private readonly string _nodeId = nodeId ?? $"{Environment.MachineName}-{Guid.NewGuid():N}"[..16];
    private readonly TimeSpan _lease = leaseDuration ?? TimeSpan.FromSeconds(15);

    public ValueTask<ILeadershipHandle?> TryAcquireLeadershipAsync(string electionId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var entry = _leaders.AddOrUpdate(electionId,
            _ => (_nodeId, now + _lease),
            (_, e) => e.Expires < now || e.Node == _nodeId ? (_nodeId, now + _lease) : e);
        return ValueTask.FromResult<ILeadershipHandle?>(entry.Node == _nodeId ? new Handle(electionId, _nodeId, this) : null);
    }

    public async ValueTask<ILeadershipHandle> AcquireLeadershipAsync(string electionId, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (await TryAcquireLeadershipAsync(electionId, ct) is { } h) return h;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException($"Failed to acquire leadership for {electionId}");
    }

    public ValueTask<bool> IsLeaderAsync(string electionId, CancellationToken ct = default)
        => ValueTask.FromResult(_leaders.TryGetValue(electionId, out var e) && e.Node == _nodeId && e.Expires > DateTime.UtcNow);

    public ValueTask<LeaderInfo?> GetLeaderAsync(string electionId, CancellationToken ct = default)
        => ValueTask.FromResult(_leaders.TryGetValue(electionId, out var e) && e.Expires > DateTime.UtcNow
            ? new LeaderInfo { NodeId = e.Node, AcquiredAt = e.Expires - _lease } : (LeaderInfo?)null);

    public async IAsyncEnumerable<LeadershipChange> WatchAsync(string electionId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        LeaderInfo? last = null;
        while (!ct.IsCancellationRequested)
        {
            var cur = await GetLeaderAsync(electionId, ct);
            if (!Equals(last, cur))
            {
                yield return new() { Type = cur.HasValue ? LeadershipChangeType.Elected : LeadershipChangeType.Lost, PreviousLeader = last, NewLeader = cur, Timestamp = DateTimeOffset.UtcNow };
                last = cur;
            }
            await Task.Delay(1000, ct);
        }
    }

    private sealed class Handle(string electionId, string nodeId, InMemoryLeaderElection e) : ILeadershipHandle
    {
        private bool _active = true;
        public string ElectionId => electionId;
        public string NodeId => nodeId;
        public bool IsLeader => _active && e._leaders.TryGetValue(electionId, out var x) && x.Node == nodeId;
        public DateTimeOffset AcquiredAt { get; } = DateTimeOffset.UtcNow;
        public event Action? OnLeadershipLost;

        public ValueTask ExtendAsync(CancellationToken ct = default)
        {
            if (e._leaders.TryGetValue(electionId, out var x) && x.Node == nodeId)
                e._leaders[electionId] = (nodeId, DateTime.UtcNow + e._lease);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _active = false;
            if (e._leaders.TryGetValue(electionId, out var x) && x.Node == nodeId) e._leaders.TryRemove(electionId, out _);
            OnLeadershipLost?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
