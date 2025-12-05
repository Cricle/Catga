using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Flow.Dsl;

/// <summary>
/// In-memory implementation of IDslFlowStore for testing and development.
/// </summary>
public class InMemoryDslFlowStore : IDslFlowStore
{
    private readonly ConcurrentDictionary<string, object> _flows = new();
    private readonly ConcurrentDictionary<string, WaitCondition> _waitConditions = new();

    public Task<bool> CreateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        var added = _flows.TryAdd(snapshot.FlowId, snapshot);
        return Task.FromResult(added);
    }

    public Task<FlowSnapshot<TState>?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        string flowId, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (_flows.TryGetValue(flowId, out var value) && value is FlowSnapshot<TState> snapshot)
        {
            return Task.FromResult<FlowSnapshot<TState>?>(snapshot);
        }
        return Task.FromResult<FlowSnapshot<TState>?>(null);
    }

    public Task<bool> UpdateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_flows.ContainsKey(snapshot.FlowId))
            return Task.FromResult(false);

        _flows[snapshot.FlowId] = snapshot;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string flowId, CancellationToken ct = default)
    {
        var removed = _flows.TryRemove(flowId, out _);
        return Task.FromResult(removed);
    }

    public Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
    {
        _waitConditions[correlationId] = condition;
        return Task.CompletedTask;
    }

    public Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
    {
        _waitConditions.TryGetValue(correlationId, out var condition);
        return Task.FromResult(condition);
    }

    public Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
    {
        _waitConditions[correlationId] = condition;
        return Task.CompletedTask;
    }

    public Task ClearWaitConditionAsync(string correlationId, CancellationToken ct = default)
    {
        _waitConditions.TryRemove(correlationId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WaitCondition>> GetTimedOutWaitConditionsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var timedOut = _waitConditions.Values
            .Where(c => now - c.CreatedAt > c.Timeout)
            .ToList();
        return Task.FromResult<IReadOnlyList<WaitCondition>>(timedOut);
    }

    /// <summary>
    /// Clear all data (for testing).
    /// </summary>
    public void Clear()
    {
        _flows.Clear();
        _waitConditions.Clear();
    }
}
