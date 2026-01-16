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
    private readonly ConcurrentDictionary<string, FlowEntry> _flows = new();
    private readonly ConcurrentDictionary<string, WaitCondition> _waitConditions = new();
    private readonly ConcurrentDictionary<string, ForEachProgress> _forEachProgress = new();

    // Internal wrapper to store flow metadata without reflection
    private sealed class FlowEntry
    {
        public required object Snapshot { get; init; }
        public required string TypeName { get; init; }
        public required DslFlowStatus Status { get; init; }
        public required DateTime CreatedAt { get; init; }
        public required DateTime UpdatedAt { get; init; }
        public required int Version { get; init; }
    }

    public Task<bool> CreateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        var entry = new FlowEntry
        {
            Snapshot = snapshot,
            TypeName = typeof(TState).FullName ?? typeof(TState).Name,
            Status = snapshot.Status,
            CreatedAt = snapshot.CreatedAt,
            UpdatedAt = snapshot.UpdatedAt,
            Version = snapshot.Version
        };
        return Task.FromResult(_flows.TryAdd(snapshot.FlowId, entry));
    }

    public Task<FlowSnapshot<TState>?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        string flowId, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_flows.TryGetValue(flowId, out var entry))
            return Task.FromResult<FlowSnapshot<TState>?>(null);

        return Task.FromResult(entry.Snapshot as FlowSnapshot<TState>);
    }

    public Task<bool> UpdateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_flows.ContainsKey(snapshot.FlowId))
            return Task.FromResult(false);

        var entry = new FlowEntry
        {
            Snapshot = snapshot,
            TypeName = typeof(TState).FullName ?? typeof(TState).Name,
            Status = snapshot.Status,
            CreatedAt = snapshot.CreatedAt,
            UpdatedAt = snapshot.UpdatedAt,
            Version = snapshot.Version
        };
        _flows[snapshot.FlowId] = entry;
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
            .Where(kvp => kvp.Value.Status == status)
            .Select(kvp => new FlowSummary(
                kvp.Key,
                kvp.Value.TypeName,
                kvp.Value.Status,
                kvp.Value.CreatedAt,
                kvp.Value.UpdatedAt,
                kvp.Value.Version))
            .ToList();
        return Task.FromResult<IReadOnlyList<FlowSummary>>(results);
    }

    public Task<IReadOnlyList<FlowSummary>> QueryByTypeAsync(string typeName, CancellationToken ct = default)
    {
        var results = _flows
            .Where(kvp => kvp.Value.TypeName == typeName || kvp.Value.TypeName.EndsWith("." + typeName))
            .Select(kvp => new FlowSummary(
                kvp.Key,
                kvp.Value.TypeName,
                kvp.Value.Status,
                kvp.Value.CreatedAt,
                kvp.Value.UpdatedAt,
                kvp.Value.Version))
            .ToList();
        return Task.FromResult<IReadOnlyList<FlowSummary>>(results);
    }

    public Task<IReadOnlyList<FlowSummary>> QueryByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var results = _flows
            .Where(kvp => kvp.Value.CreatedAt >= from && kvp.Value.CreatedAt <= to)
            .Select(kvp => new FlowSummary(
                kvp.Key,
                kvp.Value.TypeName,
                kvp.Value.Status,
                kvp.Value.CreatedAt,
                kvp.Value.UpdatedAt,
                kvp.Value.Version))
            .ToList();
        return Task.FromResult<IReadOnlyList<FlowSummary>>(results);
    }
}
