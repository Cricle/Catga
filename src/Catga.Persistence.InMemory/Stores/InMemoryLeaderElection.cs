using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Observability;
using DotNext.Threading;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>In-memory leader election using DotNext.Threading for development/testing.</summary>
public sealed class InMemoryLeaderElection(string? nodeId = null, TimeSpan? leaseDuration = null) : ILeaderElection
{
    private readonly ConcurrentDictionary<string, ElectionState> _elections = new();
    private readonly string _nodeId = nodeId ?? $"{Environment.MachineName}-{Guid.NewGuid():N}"[..16];
    private readonly TimeSpan _lease = leaseDuration ?? TimeSpan.FromSeconds(15);

    private ElectionState GetOrCreateElection(string electionId)
    {
        return _elections.GetOrAdd(electionId, _ => new ElectionState());
    }

    public async ValueTask<ILeadershipHandle?> TryAcquireLeadershipAsync(string electionId, CancellationToken ct = default)
    {
        using var activity = CatgaActivitySource.Source.StartActivity("Leader.TryAcquire", ActivityKind.Internal);
        activity?.SetTag(CatgaActivitySource.Tags.ElectionId, electionId);
        activity?.SetTag(CatgaActivitySource.Tags.LeaderNodeId, _nodeId);
        activity?.SetTag(CatgaActivitySource.Tags.LeaderLeaseDuration, _lease.TotalMilliseconds);

        var state = GetOrCreateElection(electionId);
        if (await state.Lock.TryAcquireAsync(TimeSpan.Zero, ct))
        {
            state.CurrentLeader = _nodeId;
            state.AcquiredAt = DateTimeOffset.UtcNow;
            state.ExpiresAt = DateTime.UtcNow + _lease;

            CatgaDiagnostics.LeaderElected.Add(1);
            activity?.AddActivityEvent(CatgaActivitySource.Events.LeaderAcquired, ("node_id", _nodeId));
            activity?.SetStatus(ActivityStatusCode.Ok);

            return new Handle(electionId, _nodeId, state, this);
        }

        // Check if current lease expired
        if (state.ExpiresAt < DateTime.UtcNow)
        {
            // Try to acquire again after expiry
            if (await state.Lock.TryAcquireAsync(TimeSpan.Zero, ct))
            {
                state.CurrentLeader = _nodeId;
                state.AcquiredAt = DateTimeOffset.UtcNow;
                state.ExpiresAt = DateTime.UtcNow + _lease;

                CatgaDiagnostics.LeaderElected.Add(1);
                activity?.AddActivityEvent(CatgaActivitySource.Events.LeaderAcquired, ("node_id", _nodeId));
                activity?.SetStatus(ActivityStatusCode.Ok);

                return new Handle(electionId, _nodeId, state, this);
            }
        }

        activity?.AddActivityEvent(CatgaActivitySource.Events.LeaderAcquireFailed);
        return null;
    }

    public async ValueTask<ILeadershipHandle> AcquireLeadershipAsync(string electionId, TimeSpan timeout, CancellationToken ct = default)
    {
        using var activity = CatgaActivitySource.Source.StartActivity("Leader.Acquire", ActivityKind.Internal);
        activity?.SetTag(CatgaActivitySource.Tags.ElectionId, electionId);
        activity?.SetTag(CatgaActivitySource.Tags.LeaderNodeId, _nodeId);
        activity?.SetTag(CatgaActivitySource.Tags.LeaderLeaseDuration, _lease.TotalMilliseconds);

        var state = GetOrCreateElection(electionId);
        if (await state.Lock.TryAcquireAsync(timeout, ct))
        {
            state.CurrentLeader = _nodeId;
            state.AcquiredAt = DateTimeOffset.UtcNow;
            state.ExpiresAt = DateTime.UtcNow + _lease;

            CatgaDiagnostics.LeaderElected.Add(1);
            activity?.AddActivityEvent(CatgaActivitySource.Events.LeaderAcquired, ("node_id", _nodeId));
            activity?.SetStatus(ActivityStatusCode.Ok);

            return new Handle(electionId, _nodeId, state, this);
        }

        activity?.AddActivityEvent(CatgaActivitySource.Events.LeaderAcquireTimeout);
        activity?.SetStatus(ActivityStatusCode.Error, "Leadership acquisition timeout");

        throw new TimeoutException($"Failed to acquire leadership for {electionId}");
    }

    public ValueTask<bool> IsLeaderAsync(string electionId, CancellationToken ct = default)
    {
        if (_elections.TryGetValue(electionId, out var state))
        {
            return ValueTask.FromResult(state.CurrentLeader == _nodeId && state.ExpiresAt > DateTime.UtcNow);
        }

        return ValueTask.FromResult(false);
    }

    public ValueTask<LeaderInfo?> GetLeaderAsync(string electionId, CancellationToken ct = default)
    {
        if (_elections.TryGetValue(electionId, out var state) && state.CurrentLeader != null && state.ExpiresAt > DateTime.UtcNow)
        {
            return ValueTask.FromResult<LeaderInfo?>(new LeaderInfo
            {
                NodeId = state.CurrentLeader,
                AcquiredAt = state.AcquiredAt.DateTime
            });
        }

        return ValueTask.FromResult<LeaderInfo?>(null);
    }

    public async IAsyncEnumerable<LeadershipChange> WatchAsync(string electionId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        LeaderInfo? last = null;
        while (!ct.IsCancellationRequested)
        {
            var cur = await GetLeaderAsync(electionId, ct);
            if (!Equals(last, cur))
            {
                yield return new LeadershipChange
                {
                    Type = cur.HasValue ? LeadershipChangeType.Elected : LeadershipChangeType.Lost,
                    PreviousLeader = last,
                    NewLeader = cur,
                    Timestamp = DateTimeOffset.UtcNow
                };
                last = cur;
            }
            await Task.Delay(1000, ct);
        }
    }

    private sealed class ElectionState
    {
        public AsyncExclusiveLock Lock { get; } = new();
        public string? CurrentLeader { get; set; }
        public DateTimeOffset AcquiredAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    private sealed class Handle : ILeadershipHandle
    {
        private readonly string _electionId;
        private readonly string _nodeId;
        private readonly ElectionState _state;
        private readonly InMemoryLeaderElection _election;
        private bool _active = true;

        public Handle(string electionId, string nodeId, ElectionState state, InMemoryLeaderElection election)
        {
            _electionId = electionId;
            _nodeId = nodeId;
            _state = state;
            _election = election;
            AcquiredAt = state.AcquiredAt;
        }

        public string ElectionId => _electionId;
        public string NodeId => _nodeId;
        public bool IsLeader => _active && _state.CurrentLeader == _nodeId && _state.ExpiresAt > DateTime.UtcNow;
        public DateTimeOffset AcquiredAt { get; }
        public event Action? OnLeadershipLost;

        public ValueTask ExtendAsync(CancellationToken ct = default)
        {
            if (_state.CurrentLeader == _nodeId)
            {
                _state.ExpiresAt = DateTime.UtcNow + _election._lease;
                CatgaDiagnostics.LeaderExtended.Add(1);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _active = false;
            if (_state.CurrentLeader == _nodeId)
            {
                _state.CurrentLeader = null;
                _state.Lock.Release();
                CatgaDiagnostics.LeaderLost.Add(1);
            }

            OnLeadershipLost?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
