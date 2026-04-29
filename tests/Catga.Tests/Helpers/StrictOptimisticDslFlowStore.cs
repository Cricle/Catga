using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catga.Flow;
using Catga.Flow.Dsl;

namespace Catga.Tests.Helpers;

/// <summary>
/// Test-only DSL flow store that mirrors the optimistic concurrency contract of the
/// Redis/NATS stores: callers must submit the current persisted version, and the
/// store increments the version only after a successful update.
/// </summary>
public sealed class StrictOptimisticDslFlowStore : IDslFlowStore, IDslFlowStoreVersioning
{
    public DslFlowStoreVersioningMode VersioningMode => DslFlowStoreVersioningMode.StoreAdvancesVersion;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, FlowEntry> _flows = new();
    private readonly ConcurrentDictionary<string, byte[]> _waitConditions = new();
    private readonly ConcurrentDictionary<string, byte[]> _forEachProgress = new();
    private readonly ConcurrentDictionary<string, byte[]> _parallelProgress = new();

    public int RejectedUpdateCount { get; private set; }

    private sealed record FlowEntry(
        byte[] Payload,
        string TypeName,
        DslFlowStatus Status,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        int Version);

    public Task<bool> CreateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot,
        CancellationToken ct = default)
        where TState : class, IFlowState
    {
        var stored = new StoredSnapshot<TState>(snapshot);
        var entry = new FlowEntry(
            JsonSerializer.SerializeToUtf8Bytes(stored, SerializerOptions),
            typeof(TState).FullName ?? typeof(TState).Name,
            snapshot.Status,
            snapshot.CreatedAt,
            snapshot.UpdatedAt,
            snapshot.Version);

        return Task.FromResult(_flows.TryAdd(snapshot.FlowId, entry));
    }

    public Task<FlowSnapshot<TState>?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        string flowId,
        CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_flows.TryGetValue(flowId, out var entry))
            return Task.FromResult<FlowSnapshot<TState>?>(null);

        var stored = JsonSerializer.Deserialize<StoredSnapshot<TState>>(entry.Payload, SerializerOptions);
        return Task.FromResult(stored?.ToSnapshot());
    }

    public Task<bool> UpdateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot,
        CancellationToken ct = default)
        where TState : class, IFlowState
    {
        if (!_flows.TryGetValue(snapshot.FlowId, out var currentEntry))
            return Task.FromResult(false);

        var current = JsonSerializer.Deserialize<StoredSnapshot<TState>>(currentEntry.Payload, SerializerOptions);
        if (current == null || current.Version != snapshot.Version)
        {
            RejectedUpdateCount++;
            return Task.FromResult(false);
        }

        var persisted = snapshot with
        {
            Version = snapshot.Version + 1,
            UpdatedAt = DateTime.UtcNow
        };

        var stored = new StoredSnapshot<TState>(persisted);
        var updatedEntry = new FlowEntry(
            JsonSerializer.SerializeToUtf8Bytes(stored, SerializerOptions),
            typeof(TState).FullName ?? typeof(TState).Name,
            persisted.Status,
            persisted.CreatedAt,
            persisted.UpdatedAt,
            persisted.Version);

        _flows[snapshot.FlowId] = updatedEntry;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string flowId, CancellationToken ct = default)
    {
        return Task.FromResult(_flows.TryRemove(flowId, out _));
    }

    public Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
    {
        _waitConditions[correlationId] = JsonSerializer.SerializeToUtf8Bytes(condition, SerializerOptions);
        return Task.CompletedTask;
    }

    public Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
    {
        if (!_waitConditions.TryGetValue(correlationId, out var payload))
            return Task.FromResult<WaitCondition?>(null);

        var condition = JsonSerializer.Deserialize<WaitCondition>(payload, SerializerOptions);
        return Task.FromResult(condition);
    }

    public Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
    {
        _waitConditions[correlationId] = JsonSerializer.SerializeToUtf8Bytes(condition, SerializerOptions);
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
        var results = _waitConditions.Values
            .Select(payload => JsonSerializer.Deserialize<WaitCondition>(payload, SerializerOptions))
            .Where(condition => condition != null && condition.CreatedAt.Add(condition.Timeout) <= now)
            .Cast<WaitCondition>()
            .ToList();

        return Task.FromResult<IReadOnlyList<WaitCondition>>(results);
    }

    public Task<IReadOnlyList<WaitCondition>> GetWaitConditionsByFlowAsync(string flowId, CancellationToken ct = default)
    {
        var results = _waitConditions.Values
            .Select(payload => JsonSerializer.Deserialize<WaitCondition>(payload, SerializerOptions))
            .Where(condition => condition != null && condition.FlowId == flowId)
            .Cast<WaitCondition>()
            .OrderBy(condition => condition.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<WaitCondition>>(results);
    }

    public Task SaveForEachProgressAsync(string flowId, int stepIndex, ForEachProgress progress, CancellationToken ct = default)
    {
        _forEachProgress[Catga.Persistence.PersistenceKeyHelper.ForEachKey(flowId, stepIndex)] =
            JsonSerializer.SerializeToUtf8Bytes(progress, SerializerOptions);
        return Task.CompletedTask;
    }

    public Task<ForEachProgress?> GetForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
    {
        if (!_forEachProgress.TryGetValue(Catga.Persistence.PersistenceKeyHelper.ForEachKey(flowId, stepIndex), out var payload))
            return Task.FromResult<ForEachProgress?>(null);

        var progress = JsonSerializer.Deserialize<ForEachProgress>(payload, SerializerOptions);
        return Task.FromResult(progress);
    }

    public Task ClearForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
    {
        _forEachProgress.TryRemove(Catga.Persistence.PersistenceKeyHelper.ForEachKey(flowId, stepIndex), out _);
        return Task.CompletedTask;
    }

    public Task SaveParallelProgressAsync(string flowId, int stepIndex, ParallelProgress progress, CancellationToken ct = default)
    {
        _parallelProgress[Catga.Persistence.PersistenceKeyHelper.ParallelKey(flowId, stepIndex)] =
            JsonSerializer.SerializeToUtf8Bytes(progress, SerializerOptions);
        return Task.CompletedTask;
    }

    public Task<ParallelProgress?> GetParallelProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
    {
        if (!_parallelProgress.TryGetValue(Catga.Persistence.PersistenceKeyHelper.ParallelKey(flowId, stepIndex), out var payload))
            return Task.FromResult<ParallelProgress?>(null);

        var progress = JsonSerializer.Deserialize<ParallelProgress>(payload, SerializerOptions);
        return Task.FromResult(progress);
    }

    public Task ClearParallelProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
    {
        _parallelProgress.TryRemove(Catga.Persistence.PersistenceKeyHelper.ParallelKey(flowId, stepIndex), out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FlowSummary>> QueryByStatusAsync(DslFlowStatus status, CancellationToken ct = default)
    {
        var flows = _flows
            .Where(pair => pair.Value.Status == status)
            .Select(ToSummary)
            .ToList();

        return Task.FromResult<IReadOnlyList<FlowSummary>>(flows);
    }

    public Task<IReadOnlyList<FlowSummary>> QueryByTypeAsync(string typeName, CancellationToken ct = default)
    {
        var flows = _flows
            .Where(pair => pair.Value.TypeName == typeName || pair.Value.TypeName.EndsWith("." + typeName, StringComparison.Ordinal))
            .Select(ToSummary)
            .ToList();

        return Task.FromResult<IReadOnlyList<FlowSummary>>(flows);
    }

    public Task<IReadOnlyList<FlowSummary>> QueryByDateRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var flows = _flows
            .Where(pair => pair.Value.CreatedAt >= from && pair.Value.CreatedAt <= to)
            .Select(ToSummary)
            .ToList();

        return Task.FromResult<IReadOnlyList<FlowSummary>>(flows);
    }

    private static FlowSummary ToSummary(KeyValuePair<string, FlowEntry> pair)
    {
        var entry = pair.Value;
        return new FlowSummary(
            pair.Key,
            entry.TypeName,
            entry.Status,
            entry.CreatedAt,
            entry.UpdatedAt,
            entry.Version);
    }
}
