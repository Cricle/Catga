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
    private readonly ConcurrentDictionary<string, ForEachProgress> _forEachProgress = new();
    private readonly ConcurrentDictionary<string, LoopProgress> _loopProgress = new();

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

    public Task SaveForEachProgressAsync(string flowId, int stepIndex, ForEachProgress progress, CancellationToken ct = default)
    {
        var key = $"{flowId}:{stepIndex}";
        _forEachProgress.AddOrUpdate(key, progress, (_, _) => progress);
        return Task.CompletedTask;
    }

    public Task<ForEachProgress?> GetForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
    {
        var key = $"{flowId}:{stepIndex}";
        _forEachProgress.TryGetValue(key, out var progress);
        return Task.FromResult(progress);
    }

    public Task ClearForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
    {
        var key = $"{flowId}:{stepIndex}";
        _forEachProgress.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task SaveLoopProgressAsync(string flowId, int stepIndex, LoopProgress progress, CancellationToken ct = default)
    {
        var key = $"{flowId}:{stepIndex}";
        _loopProgress.AddOrUpdate(key, progress, (_, _) => progress);
        return Task.CompletedTask;
    }

    public Task<LoopProgress?> GetLoopProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
    {
        var key = $"{flowId}:{stepIndex}";
        _loopProgress.TryGetValue(key, out var progress);
        return Task.FromResult(progress);
    }

    public Task ClearLoopProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
    {
        var key = $"{flowId}:{stepIndex}";
        _loopProgress.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Clear all data (for testing).
    /// </summary>
    public void Clear()
    {
        _flows.Clear();
        _waitConditions.Clear();
        _forEachProgress.Clear();
        _loopProgress.Clear();
    }
}
