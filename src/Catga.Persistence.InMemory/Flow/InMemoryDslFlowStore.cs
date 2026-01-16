using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;

namespace Catga.Persistence.InMemory.Flow;

/// <summary>
/// In-memory DSL flow store for development/testing.
/// </summary>
public sealed class InMemoryDslFlowStore : IDslFlowStore
{
    private readonly ConcurrentDictionary<string, object> _flows = new();
    private readonly ConcurrentDictionary<string, WaitCondition> _waitConditions = new();
    private readonly ConcurrentDictionary<string, ForEachProgress> _forEachProgress = new();

    public Task<bool> CreateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        return Task.FromResult(_flows.TryAdd(snapshot.FlowId, snapshot));
    }

    public Task<FlowSnapshot<TState>?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        string flowId, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_flows.TryGetValue(flowId, out var entry))
            return Task.FromResult<FlowSnapshot<TState>?>(null);

        return Task.FromResult<FlowSnapshot<TState>?>(entry as FlowSnapshot<TState>);
    }

    public Task<bool> UpdateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_flows.ContainsKey(snapshot.FlowId))
            return Task.FromResult(false);

        _flows[snapshot.FlowId] = snapshot;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string flowId, CancellationToken ct = default)
    {
        return Task.FromResult(_flows.TryRemove(flowId, out _));
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

    public Task<IReadOnlyList<FlowSummary>> QueryByStatusAsync(DslFlowStatus status, CancellationToken ct = default)
    {
        var results = _flows
            .Where(kvp =>
            {
                var type = kvp.Value.GetType();
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(FlowSnapshot<>))
                    return false;
                
                var statusProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.Status));
                if (statusProp == null) return false;
                
                var flowStatus = (DslFlowStatus)statusProp.GetValue(kvp.Value)!;
                return flowStatus == status;
            })
            .Select(kvp =>
            {
                var type = kvp.Value.GetType();
                var stateProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.State))!;
                var statusProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.Status))!;
                var createdAtProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.CreatedAt))!;
                var updatedAtProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.UpdatedAt))!;
                var versionProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.Version))!;
                
                var state = (IFlowState)stateProp.GetValue(kvp.Value)!;
                var flowStatus = (DslFlowStatus)statusProp.GetValue(kvp.Value)!;
                var createdAt = (DateTime)createdAtProp.GetValue(kvp.Value)!;
                var updatedAt = (DateTime)updatedAtProp.GetValue(kvp.Value)!;
                var version = (int)versionProp.GetValue(kvp.Value)!;
                
                return new FlowSummary(
                    kvp.Key,
                    state.GetType().FullName ?? state.GetType().Name,
                    flowStatus,
                    createdAt,
                    updatedAt,
                    version);
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<FlowSummary>>(results);
    }

    public Task<IReadOnlyList<FlowSummary>> QueryByTypeAsync(string typeName, CancellationToken ct = default)
    {
        var results = _flows
            .Where(kvp =>
            {
                var type = kvp.Value.GetType();
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(FlowSnapshot<>))
                    return false;
                
                var stateProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.State));
                if (stateProp == null) return false;
                
                var state = (IFlowState)stateProp.GetValue(kvp.Value)!;
                var stateType = state.GetType();
                // Support both full name and short name matching
                return stateType.FullName == typeName || stateType.Name == typeName;
            })
            .Select(kvp =>
            {
                var type = kvp.Value.GetType();
                var stateProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.State))!;
                var statusProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.Status))!;
                var createdAtProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.CreatedAt))!;
                var updatedAtProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.UpdatedAt))!;
                var versionProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.Version))!;
                
                var state = (IFlowState)stateProp.GetValue(kvp.Value)!;
                var flowStatus = (DslFlowStatus)statusProp.GetValue(kvp.Value)!;
                var createdAt = (DateTime)createdAtProp.GetValue(kvp.Value)!;
                var updatedAt = (DateTime)updatedAtProp.GetValue(kvp.Value)!;
                var version = (int)versionProp.GetValue(kvp.Value)!;
                
                return new FlowSummary(
                    kvp.Key,
                    state.GetType().FullName ?? state.GetType().Name,
                    flowStatus,
                    createdAt,
                    updatedAt,
                    version);
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<FlowSummary>>(results);
    }

    public Task<IReadOnlyList<FlowSummary>> QueryByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var results = _flows
            .Where(kvp =>
            {
                var type = kvp.Value.GetType();
                if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(FlowSnapshot<>))
                    return false;
                
                var createdAtProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.CreatedAt));
                if (createdAtProp == null) return false;
                
                var createdAt = (DateTime)createdAtProp.GetValue(kvp.Value)!;
                return createdAt >= from && createdAt <= to;
            })
            .Select(kvp =>
            {
                var type = kvp.Value.GetType();
                var stateProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.State))!;
                var statusProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.Status))!;
                var createdAtProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.CreatedAt))!;
                var updatedAtProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.UpdatedAt))!;
                var versionProp = type.GetProperty(nameof(FlowSnapshot<IFlowState>.Version))!;
                
                var state = (IFlowState)stateProp.GetValue(kvp.Value)!;
                var flowStatus = (DslFlowStatus)statusProp.GetValue(kvp.Value)!;
                var createdAt = (DateTime)createdAtProp.GetValue(kvp.Value)!;
                var updatedAt = (DateTime)updatedAtProp.GetValue(kvp.Value)!;
                var version = (int)versionProp.GetValue(kvp.Value)!;
                
                return new FlowSummary(
                    kvp.Key,
                    state.GetType().FullName ?? state.GetType().Name,
                    flowStatus,
                    createdAt,
                    updatedAt,
                    version);
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<FlowSummary>>(results);
    }
}
