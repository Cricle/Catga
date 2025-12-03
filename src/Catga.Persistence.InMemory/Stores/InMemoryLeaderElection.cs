using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Catga.Abstractions;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>
/// In-memory leader election for development and testing.
/// </summary>
public sealed class InMemoryLeaderElection : ILeaderElection
{
    private readonly ConcurrentDictionary<string, LeaderEntry> _leaders = new();
    private readonly string _nodeId;
    private readonly TimeSpan _leaseDuration;

    public InMemoryLeaderElection(string? nodeId = null, TimeSpan? leaseDuration = null)
    {
        _nodeId = nodeId ?? $"{Environment.MachineName}-{Guid.NewGuid():N}"[..16];
        _leaseDuration = leaseDuration ?? TimeSpan.FromSeconds(15);
    }

    public ValueTask<ILeadershipHandle?> TryAcquireLeadershipAsync(string electionId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var entry = _leaders.AddOrUpdate(
            electionId,
            _ => new LeaderEntry(_nodeId, now + _leaseDuration),
            (_, existing) =>
            {
                if (existing.ExpiresAt < now || existing.NodeId == _nodeId)
                    return new LeaderEntry(_nodeId, now + _leaseDuration);
                return existing;
            });

        if (entry.NodeId == _nodeId)
        {
            return ValueTask.FromResult<ILeadershipHandle?>(
                new InMemoryLeadershipHandle(electionId, _nodeId, this));
        }
        return ValueTask.FromResult<ILeadershipHandle?>(null);
    }

    public async ValueTask<ILeadershipHandle> AcquireLeadershipAsync(string electionId, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var handle = await TryAcquireLeadershipAsync(electionId, ct);
            if (handle != null) return handle;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException($"Failed to acquire leadership for {electionId}");
    }

    public ValueTask<bool> IsLeaderAsync(string electionId, CancellationToken ct = default)
    {
        if (_leaders.TryGetValue(electionId, out var entry))
        {
            return ValueTask.FromResult(entry.NodeId == _nodeId && entry.ExpiresAt > DateTime.UtcNow);
        }
        return ValueTask.FromResult(false);
    }

    public ValueTask<LeaderInfo?> GetLeaderAsync(string electionId, CancellationToken ct = default)
    {
        if (_leaders.TryGetValue(electionId, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
        {
            return ValueTask.FromResult<LeaderInfo?>(new LeaderInfo
            {
                NodeId = entry.NodeId,
                AcquiredAt = entry.ExpiresAt - _leaseDuration
            });
        }
        return ValueTask.FromResult<LeaderInfo?>(null);
    }

    public async IAsyncEnumerable<LeadershipChange> WatchAsync(
        string electionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        LeaderInfo? lastLeader = null;
        while (!ct.IsCancellationRequested)
        {
            var current = await GetLeaderAsync(electionId, ct);
            if (!Equals(lastLeader, current))
            {
                yield return new LeadershipChange
                {
                    Type = current.HasValue ? LeadershipChangeType.Elected : LeadershipChangeType.Lost,
                    PreviousLeader = lastLeader,
                    NewLeader = current,
                    Timestamp = DateTimeOffset.UtcNow
                };
                lastLeader = current;
            }
            await Task.Delay(1000, ct);
        }
    }

    internal void Extend(string electionId)
    {
        if (_leaders.TryGetValue(electionId, out var entry) && entry.NodeId == _nodeId)
        {
            _leaders[electionId] = new LeaderEntry(_nodeId, DateTime.UtcNow + _leaseDuration);
        }
    }

    internal void Release(string electionId)
    {
        if (_leaders.TryGetValue(electionId, out var entry) && entry.NodeId == _nodeId)
        {
            _leaders.TryRemove(electionId, out _);
        }
    }

    private readonly record struct LeaderEntry(string NodeId, DateTime ExpiresAt);

    private sealed class InMemoryLeadershipHandle : ILeadershipHandle
    {
        private readonly InMemoryLeaderElection _election;
        private bool _isLeader = true;

        public string ElectionId { get; }
        public string NodeId { get; }
        public bool IsLeader => _isLeader && _election._leaders.TryGetValue(ElectionId, out var e) && e.NodeId == NodeId;
        public DateTimeOffset AcquiredAt { get; } = DateTimeOffset.UtcNow;
        public event Action? OnLeadershipLost;

        public InMemoryLeadershipHandle(string electionId, string nodeId, InMemoryLeaderElection election)
        {
            ElectionId = electionId;
            NodeId = nodeId;
            _election = election;
        }

        public ValueTask ExtendAsync(CancellationToken ct = default)
        {
            _election.Extend(ElectionId);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _isLeader = false;
            _election.Release(ElectionId);
            OnLeadershipLost?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
