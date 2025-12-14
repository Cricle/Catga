using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Persistence;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Client.JetStream;

namespace Catga.Persistence.Nats.Flow;

/// <summary>
/// NATS KV-based DSL flow store with revision-based optimistic locking.
/// Supports distributed flow execution with WaitCondition for WhenAll/WhenAny.
/// </summary>
public sealed class NatsDslFlowStore : IDslFlowStore
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly string _bucketName;
    private readonly string _waitBucket;
    private INatsKVContext? _kv;
    private INatsKVStore? _store;
    private INatsKVStore? _waitStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public NatsDslFlowStore(INatsConnection nats, IMessageSerializer serializer, string bucketName = "dslflows")
    {
        _nats = nats;
        _serializer = serializer;
        _bucketName = bucketName;
        _waitBucket = $"{bucketName}_wait";
    }

    private static string EncodeKey(string id) => PersistenceKeyHelper.EncodeNatsKey(id);
    private static string EncodeKey(string flowId, int stepIndex) => PersistenceKeyHelper.EncodeNatsForEachKey(flowId, stepIndex);

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            _kv = new NatsKVContext(new NatsJSContext(_nats));

            _store = await _kv.CreateStoreAsync(new NatsKVConfig(_bucketName)
            {
                History = 1,
                Storage = NatsKVStorageType.File,
                MaxAge = TimeSpan.FromDays(7)
            }, ct);

            _waitStore = await _kv.CreateStoreAsync(new NatsKVConfig(_waitBucket)
            {
                History = 1,
                Storage = NatsKVStorageType.File,
                MaxAge = TimeSpan.FromDays(1)
            }, ct);

            _initialized = true;
        }
        catch (NatsKVException) { _initialized = true; }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<bool> CreateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(snapshot.FlowId);
        var stored = new StoredSnapshot<TState>(snapshot);
        var data = _serializer.Serialize(stored);

        try
        {
            await _store!.CreateAsync(key, data, cancellationToken: ct);
            return true;
        }
        catch (NatsKVCreateException)
        {
            return false;
        }
    }

    public async Task<FlowSnapshot<TState>?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        string flowId, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(flowId);
        try
        {
            var entry = await _store!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
            if (entry.Value == null) return null;

            var stored = _serializer.Deserialize<StoredSnapshot<TState>>(entry.Value);
            return stored?.ToSnapshot();
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> UpdateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(
        FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(snapshot.FlowId);

        NatsKVEntry<byte[]> entry;
        try
        {
            entry = await _store!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
        }
        catch (NatsKVKeyNotFoundException)
        {
            return false;
        }

        var current = _serializer.Deserialize<StoredSnapshot<TState>>(entry.Value!);
        if (current == null || current.Version != snapshot.Version)
            return false;

        var newSnapshot = snapshot with { Version = snapshot.Version + 1, UpdatedAt = DateTime.UtcNow };
        var stored = new StoredSnapshot<TState>(newSnapshot);
        var data = _serializer.Serialize(stored);

        try
        {
            await _store!.UpdateAsync(key, data, entry.Revision, cancellationToken: ct);
            return true;
        }
        catch (NatsKVWrongLastRevisionException)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string flowId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(flowId);
        try
        {
            await _store!.DeleteAsync(key, cancellationToken: ct);
            return true;
        }
        catch (NatsKVKeyNotFoundException)
        {
            return false;
        }
    }

    public async Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(correlationId);
        var data = _serializer.Serialize(condition);

        try
        {
            await _waitStore!.CreateAsync(key, data, cancellationToken: ct);
        }
        catch (NatsKVCreateException)
        {
            // Already exists, update it
            try
            {
                var entry = await _waitStore!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
                await _waitStore!.UpdateAsync(key, data, entry.Revision, cancellationToken: ct);
            }
            catch { /* ignore */ }
        }
    }

    public async Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(correlationId);
        try
        {
            var entry = await _waitStore!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
            if (entry.Value == null) return null;
            return _serializer.Deserialize<WaitCondition>(entry.Value);
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
    }

    public async Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(correlationId);
        var data = _serializer.Serialize(condition);

        try
        {
            var entry = await _waitStore!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
            await _waitStore!.UpdateAsync(key, data, entry.Revision, cancellationToken: ct);
        }
        catch (NatsKVKeyNotFoundException)
        {
            await _waitStore!.CreateAsync(key, data, cancellationToken: ct);
        }
        catch (NatsKVWrongLastRevisionException)
        {
            // Retry once
            var entry = await _waitStore!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
            await _waitStore!.UpdateAsync(key, data, entry.Revision, cancellationToken: ct);
        }
    }

    public async Task ClearWaitConditionAsync(string correlationId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(correlationId);
        try
        {
            await _waitStore!.DeleteAsync(key, cancellationToken: ct);
        }
        catch (NatsKVKeyNotFoundException) { /* already deleted */ }
    }

    public async Task<IReadOnlyList<WaitCondition>> GetTimedOutWaitConditionsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var results = new List<WaitCondition>();
        var now = DateTime.UtcNow;

        // Iterate all keys in wait bucket
        await foreach (var key in _waitStore!.GetKeysAsync(cancellationToken: ct))
        {
            try
            {
                var entry = await _waitStore!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
                if (entry.Value == null) continue;

                var condition = _serializer.Deserialize<WaitCondition>(entry.Value);
                if (condition != null && condition.CreatedAt.Add(condition.Timeout) <= now)
                {
                    results.Add(condition);
                }
            }
            catch { /* ignore individual failures */ }
        }

        return results;
    }

    public async Task SaveForEachProgressAsync(string flowId, int stepIndex, ForEachProgress progress, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(flowId, stepIndex);
        var data = _serializer.Serialize(progress);

        try
        {
            await _store!.CreateAsync(key, data, cancellationToken: ct);
        }
        catch (NatsKVCreateException)
        {
            // Already exists, update it
            try
            {
                var entry = await _store!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
                await _store!.UpdateAsync(key, data, entry.Revision, cancellationToken: ct);
            }
            catch { /* ignore */ }
        }
    }

    public async Task<ForEachProgress?> GetForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(flowId, stepIndex);
        try
        {
            var entry = await _store!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
            if (entry.Value == null) return null;
            return _serializer.Deserialize<ForEachProgress>(entry.Value);
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
    }

    public async Task ClearForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(flowId, stepIndex);
        try
        {
            await _store!.DeleteAsync(key, cancellationToken: ct);
        }
        catch (NatsKVKeyNotFoundException) { /* already deleted */ }
    }

}
