using System.Collections.Concurrent;
using Catga.Flow;

namespace Catga.Persistence.InMemory.Flow;

/// <summary>
/// In-memory flow store. Lock-free with CAS.
/// For development/testing. Use Redis/NATS for production clusters.
/// </summary>
public sealed class InMemoryFlowStore : IFlowStore
{
    private readonly ConcurrentDictionary<string, FlowStateEntry> _flows = new();
    private readonly ConcurrentDictionary<string, List<string>> _byType = new();

    public ValueTask<bool> CreateAsync(FlowState state, CancellationToken ct = default)
    {
        var entry = new FlowStateEntry(state);
        if (!_flows.TryAdd(state.Id, entry))
            return ValueTask.FromResult(false);

        // Index by type for TryClaimAsync
        _byType.AddOrUpdate(
            state.Type,
            _ => [state.Id],
            (_, list) => { lock (list) { list.Add(state.Id); } return list; });

        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> UpdateAsync(FlowState state, CancellationToken ct = default)
    {
        if (!_flows.TryGetValue(state.Id, out var entry))
            return ValueTask.FromResult(false);

        // CAS: check version - compare state.Version with entry.Version
        var expectedVersion = state.Version;
        if (Interlocked.CompareExchange(ref entry.Version, expectedVersion + 1, expectedVersion) != expectedVersion)
            return ValueTask.FromResult(false);

        // Update fields
        entry.State.Status = state.Status;
        entry.State.Step = state.Step;
        entry.State.Owner = state.Owner;
        entry.State.HeartbeatAt = state.HeartbeatAt;
        entry.State.Error = state.Error;
        entry.State.Data = state.Data;
        state.Version = entry.Version;

        return ValueTask.FromResult(true);
    }

    public ValueTask<FlowState?> GetAsync(string id, CancellationToken ct = default)
    {
        if (_flows.TryGetValue(id, out var entry))
        {
            entry.State.Version = entry.Version;
            return ValueTask.FromResult<FlowState?>(entry.State);
        }
        return ValueTask.FromResult<FlowState?>(null);
    }

    public ValueTask<FlowState?> TryClaimAsync(string type, string owner, long timeoutMs, CancellationToken ct = default)
    {
        if (!_byType.TryGetValue(type, out var ids))
            return ValueTask.FromResult<FlowState?>(null);

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (ids)
        {
            foreach (var id in ids)
            {
                if (!_flows.TryGetValue(id, out var entry))
                    continue;

                // Skip completed/failed
                if (entry.State.Status is FlowStatus.Done or FlowStatus.Failed)
                    continue;

                // Check if abandoned (heartbeat timeout)
                if (nowMs - entry.State.HeartbeatAt < timeoutMs)
                    continue;

                // CAS claim
                var oldVersion = entry.Version;
                if (Interlocked.CompareExchange(ref entry.Version, oldVersion + 1, oldVersion) == oldVersion)
                {
                    entry.State.Owner = owner;
                    entry.State.HeartbeatAt = nowMs;
                    entry.State.Version = entry.Version;
                    return ValueTask.FromResult<FlowState?>(entry.State);
                }
            }
        }

        return ValueTask.FromResult<FlowState?>(null);
    }

    public ValueTask<bool> HeartbeatAsync(string id, string owner, long version, CancellationToken ct = default)
    {
        if (!_flows.TryGetValue(id, out var entry))
            return ValueTask.FromResult(false);

        // Verify owner
        if (entry.State.Owner != owner)
            return ValueTask.FromResult(false);

        // CAS update heartbeat
        if (Interlocked.CompareExchange(ref entry.Version, version + 1, version) != version)
            return ValueTask.FromResult(false);

        entry.State.HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return ValueTask.FromResult(true);
    }

    private sealed class FlowStateEntry(FlowState state)
    {
        public FlowState State { get; } = state;
        public long Version;
    }
}
