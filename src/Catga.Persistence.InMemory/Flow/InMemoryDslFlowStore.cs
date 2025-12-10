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
    private readonly IMessageSerializer _serializer;
    private readonly ConcurrentDictionary<string, FlowEntry> _flows = new();
    private readonly ConcurrentDictionary<string, WaitCondition> _waitConditions = new();
    private readonly ConcurrentDictionary<string, ForEachProgress> _forEachProgress = new();

    public InMemoryDslFlowStore(IMessageSerializer serializer)
    {
        _serializer = serializer;
    }

    public Task<bool> CreateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        var data = _serializer.Serialize(snapshot);
        var entry = new FlowEntry(typeof(TState).FullName ?? typeof(TState).Name, data, 0);
        return Task.FromResult(_flows.TryAdd(snapshot.FlowId, entry));
    }

    public Task<FlowSnapshot<TState>?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        string flowId, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_flows.TryGetValue(flowId, out var entry))
            return Task.FromResult<FlowSnapshot<TState>?>(null);

        var snapshot = _serializer.Deserialize<FlowSnapshot<TState>>(entry.Data);
        return Task.FromResult(snapshot);
    }

    public Task<bool> UpdateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_flows.TryGetValue(snapshot.FlowId, out var entry))
            return Task.FromResult(false);

        var expectedVersion = snapshot.Version - 1;
        if (Interlocked.CompareExchange(ref entry.Version, snapshot.Version, expectedVersion) != expectedVersion)
            return Task.FromResult(false);

        entry.Data = _serializer.Serialize(snapshot);
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

    private sealed class FlowEntry
    {
        public string TypeName { get; }
        public byte[] Data { get; set; }
        public long Version;

        public FlowEntry(string typeName, byte[] data, long version)
        {
            TypeName = typeName;
            Data = data;
            Version = version;
        }
    }
}
