using Catga.Abstractions;
using Catga.Flow;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence.Nats.Flow;

/// <summary>
/// NATS KV-based flow store with revision-based optimistic locking.
/// Production-ready for distributed clusters.
/// </summary>
public sealed class NatsFlowStore : IFlowStore
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly string _bucketName;
    private readonly string _indexBucket;
    private INatsKVContext? _kv;
    private INatsKVStore? _store;
    private INatsKVStore? _indexStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public NatsFlowStore(INatsConnection nats, IMessageSerializer serializer, string bucketName = "flows")
    {
        _nats = nats;
        _serializer = serializer;
        _bucketName = bucketName;
        _indexBucket = $"{bucketName}_idx";
    }

    // Encode flow ID for NATS key (replace special chars)
    private static string EncodeId(string id) => id.Replace(":", "_C_").Replace("/", "_S_").Replace(".", "_D_");
    private static string DecodeId(string encoded) => encoded.Replace("_C_", ":").Replace("_S_", "/").Replace("_D_", ".");

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            _kv = new NatsKVContext(new NatsJSContext(_nats));

            // Main flow state bucket
            _store = await _kv.CreateStoreAsync(new NatsKVConfig(_bucketName)
            {
                History = 1,
                Storage = NatsKVStorageType.File,
                MaxAge = TimeSpan.FromDays(7)
            }, ct);

            // Type index bucket (stores flow IDs by type)
            _indexStore = await _kv.CreateStoreAsync(new NatsKVConfig(_indexBucket)
            {
                History = 64, // Keep some history for index updates
                Storage = NatsKVStorageType.File,
                MaxAge = TimeSpan.FromDays(7)
            }, ct);

            _initialized = true;
        }
        catch (NatsKVException) { _initialized = true; } // Bucket exists
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask<bool> CreateAsync(FlowState state, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeId(state.Id);
        var data = _serializer.Serialize(state);

        try
        {
            // Create with revision 0 (must not exist)
            await _store!.CreateAsync(key, data, cancellationToken: ct);

            // Add to type index
            await AddToTypeIndexAsync(state.Type, state.Id, ct);
            return true;
        }
        catch (NatsKVCreateException)
        {
            return false; // Already exists
        }
    }

    public async ValueTask<bool> UpdateAsync(FlowState state, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeId(state.Id);

        // Get current revision
        NatsKVEntry<byte[]> entry;
        try
        {
            entry = await _store!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
        }
        catch (NatsKVKeyNotFoundException)
        {
            return false;
        }

        // Deserialize to check version
        var current = _serializer.Deserialize<FlowState>(entry.Value!);
        if (current == null || current.Version != state.Version)
            return false;

        // Create updated state
        var newVersion = state.Version + 1;
        var updatedState = new FlowState
        {
            Id = state.Id,
            Type = state.Type,
            Status = state.Status,
            Step = state.Step,
            Version = newVersion,
            Owner = state.Owner,
            HeartbeatAt = state.HeartbeatAt,
            Data = state.Data,
            Error = state.Error
        };
        var data = _serializer.Serialize(updatedState);

        try
        {
            // CAS update with expected revision
            await _store!.UpdateAsync(key, data, entry.Revision, cancellationToken: ct);
            state.Version = newVersion;
            return true;
        }
        catch (NatsKVWrongLastRevisionException)
        {
            return false; // Concurrent modification
        }
    }

    public async ValueTask<FlowState?> GetAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeId(id);
        try
        {
            var entry = await _store!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
            if (entry.Value == null) return null;
            return _serializer.Deserialize<FlowState>(entry.Value);
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
    }

    public async ValueTask<FlowState?> TryClaimAsync(string type, string owner, long timeoutMs, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Get flow IDs for this type from index
        var flowIds = await GetTypeIndexAsync(type, ct);

        foreach (var id in flowIds)
        {
            var state = await GetAsync(id, ct);
            if (state == null) continue;
            if (state.Status is FlowStatus.Done or FlowStatus.Failed) continue;
            if (nowMs - state.HeartbeatAt < timeoutMs) continue;

            // Try CAS claim
            var claimState = new FlowState
            {
                Id = state.Id,
                Type = state.Type,
                Status = state.Status,
                Step = state.Step,
                Version = state.Version,
                Owner = owner,
                HeartbeatAt = nowMs,
                Data = state.Data,
                Error = state.Error
            };

            if (await UpdateAsync(claimState, ct))
            {
                state.Owner = owner;
                state.HeartbeatAt = nowMs;
                state.Version = claimState.Version;
                return state;
            }
        }

        return null;
    }

    public async ValueTask<bool> HeartbeatAsync(string id, string owner, long version, CancellationToken ct = default)
    {
        var state = await GetAsync(id, ct);
        if (state == null || state.Owner != owner || state.Version != version)
            return false;

        state.HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return await UpdateAsync(state, ct);
    }

    // Index management
    private async ValueTask AddToTypeIndexAsync(string type, string flowId, CancellationToken ct)
    {
        var indexKey = $"type_{EncodeId(type)}";
        try
        {
            var entry = await _indexStore!.GetEntryAsync<byte[]>(indexKey, cancellationToken: ct);
            var ids = _serializer.Deserialize<HashSet<string>>(entry.Value!) ?? [];
            ids.Add(flowId);
            await _indexStore!.UpdateAsync(indexKey, _serializer.Serialize(ids), entry.Revision, cancellationToken: ct);
        }
        catch (NatsKVKeyNotFoundException)
        {
            var ids = new HashSet<string> { flowId };
            try
            {
                await _indexStore!.CreateAsync(indexKey, _serializer.Serialize(ids), cancellationToken: ct);
            }
            catch (NatsKVCreateException)
            {
                // Race condition, retry
                await AddToTypeIndexAsync(type, flowId, ct);
            }
        }
        catch (NatsKVWrongLastRevisionException)
        {
            // Retry on conflict
            await AddToTypeIndexAsync(type, flowId, ct);
        }
    }

    private async ValueTask<IEnumerable<string>> GetTypeIndexAsync(string type, CancellationToken ct)
    {
        var indexKey = $"type_{EncodeId(type)}";
        try
        {
            var entry = await _indexStore!.GetEntryAsync<byte[]>(indexKey, cancellationToken: ct);
            return _serializer.Deserialize<HashSet<string>>(entry.Value!) ?? [];
        }
        catch (NatsKVKeyNotFoundException)
        {
            return [];
        }
    }
}
