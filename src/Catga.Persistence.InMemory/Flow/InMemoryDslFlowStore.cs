using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;

namespace Catga.Persistence.InMemory.Flow;

/// <summary>
/// In-memory DSL flow store for development/testing.
/// </summary>
public class InMemoryDslFlowStore : IDslFlowStore
{
    private readonly ConcurrentDictionary<string, object> _snapshots = new();
    private readonly ConcurrentDictionary<string, WaitCondition> _waitConditions = new();
    private readonly ConcurrentDictionary<string, ForEachProgress> _forEachProgress = new();
    private readonly ConcurrentDictionary<string, LoopProgress> _loopProgress = new();
    private readonly ConcurrentDictionary<string, long> _versions = new();

    public InMemoryDslFlowStore() { }

    public InMemoryDslFlowStore(IMessageSerializer? serializer) { }

    public Task<bool> CreateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_snapshots.TryAdd(snapshot.FlowId, snapshot))
            return Task.FromResult(false);
        _versions[snapshot.FlowId] = 0;
        return Task.FromResult(true);
    }

    public Task<FlowSnapshot<TState>?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        string flowId, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_snapshots.TryGetValue(flowId, out var obj))
            return Task.FromResult<FlowSnapshot<TState>?>(null);
        return Task.FromResult(obj as FlowSnapshot<TState>);
    }

    public Task<bool> UpdateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_snapshots.ContainsKey(snapshot.FlowId))
            return Task.FromResult(false);
        _snapshots[snapshot.FlowId] = snapshot;
        _versions[snapshot.FlowId] = snapshot.Version;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string flowId, CancellationToken ct = default)
    {
        _versions.TryRemove(flowId, out _);
        return Task.FromResult(_snapshots.TryRemove(flowId, out _));
    }

    public Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
    {
        _waitConditions[correlationId] = condition;
        return Task.CompletedTask;
    }

    public Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
    {
        return Task.FromResult(_waitConditions.TryGetValue(correlationId, out var condition) ? condition : null);
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
            .Where(c => c.CreatedAt + c.Timeout < now)
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

}
