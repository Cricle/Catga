using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace Catga.Persistence.Nats;

/// <summary>
/// NATS KV-based enhanced snapshot store with multi-version support.
/// Uses a separate key for each version to support version-based queries.
/// </summary>
public sealed class NatsEnhancedSnapshotStore : IEnhancedSnapshotStore
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;
    private readonly string _bucketName;
    private volatile INatsKVStore? _kvStore;
    private Task? _initTask;

    public NatsEnhancedSnapshotStore(
        INatsConnection nats,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        string bucketName = "enhanced-snapshots")
    {
        _nats = nats;
        _serializer = serializer;
        _provider = provider;
        _bucketName = bucketName;
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_kvStore != null) return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = Interlocked.CompareExchange(ref _initTask, tcs.Task, null);
        if (existing != null)
        {
            await existing.WaitAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var kv = new NatsKVContext(new NatsJSContext(_nats));
            try
            {
                _kvStore = await kv.GetStoreAsync(_bucketName, ct);
            }
            catch (NatsKVException)
            {
                _kvStore = await kv.CreateStoreAsync(new NatsKVConfig(_bucketName)
                {
                    History = 64 // Keep history for version queries
                }, ct);
            }
            tcs.SetResult();
        }
        catch (Exception ex)
        {
            _ = Interlocked.Exchange(ref _initTask, null);
            tcs.SetException(ex);
            throw;
        }
    }

    public async ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        TAggregate aggregate,
        long version,
        CancellationToken ct = default) where TAggregate : class
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            var data = new SnapshotData
            {
                Version = version,
                TimestampTicks = DateTime.UtcNow.Ticks,
                TypeName = typeof(TAggregate).AssemblyQualifiedName!,
                Data = _serializer.Serialize(aggregate)
            };

            var bytes = _serializer.Serialize(data);
            var key = $"{streamId}.v{version:D10}";
            await _kvStore!.PutAsync(key, bytes, cancellationToken: ct);

            // Also update latest pointer
            await _kvStore.PutAsync($"{streamId}.latest", BitConverter.GetBytes(version), cancellationToken: ct);
        }, ct);
    }

    public async ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        CancellationToken ct = default) where TAggregate : class
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            try
            {
                // Get latest version
                var latestEntry = await _kvStore!.GetEntryAsync<byte[]>($"{streamId}.latest", cancellationToken: ct);
                if (latestEntry.Value == null) return null;

                var latestVersion = BitConverter.ToInt64(latestEntry.Value);
                return await LoadAtVersionInternalAsync<TAggregate>(streamId, latestVersion, ct);
            }
            catch (NatsKVKeyNotFoundException)
            {
                return null;
            }
        }, ct);
    }

    public async ValueTask<Snapshot<TAggregate>?> LoadAtVersionAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        long version,
        CancellationToken ct = default) where TAggregate : class
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            // Find the highest version <= target
            var keys = await GetSnapshotKeysAsync(streamId, ct);
            var targetKey = keys
                .Where(k => k.version <= version)
                .OrderByDescending(k => k.version)
                .FirstOrDefault();

            if (targetKey.key == null) return null;

            return await LoadAtVersionInternalAsync<TAggregate>(streamId, targetKey.version, ct);
        }, ct);
    }

    private async ValueTask<Snapshot<TAggregate>?> LoadAtVersionInternalAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        long version,
        CancellationToken ct) where TAggregate : class
    {
        try
        {
            var key = $"{streamId}.v{version:D10}";
            var entry = await _kvStore!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
            if (entry.Value == null) return null;

            var data = (SnapshotData?)_serializer.Deserialize(entry.Value, typeof(SnapshotData));
            if (data == null) return null;

            var state = (TAggregate?)_serializer.Deserialize(data.Data, typeof(TAggregate));
            if (state == null) return null;

            return new Snapshot<TAggregate>
            {
                StreamId = streamId,
                State = state,
                Version = data.Version,
                Timestamp = new DateTime(data.TimestampTicks, DateTimeKind.Utc)
            };
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
    }

    public async ValueTask<IReadOnlyList<SnapshotInfo>> GetSnapshotHistoryAsync(
        string streamId,
        CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            var keys = await GetSnapshotKeysAsync(streamId, ct);
            var history = new List<SnapshotInfo>();

            foreach (var (key, version) in keys.OrderBy(k => k.version))
            {
                try
                {
                    var entry = await _kvStore!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
                    if (entry.Value != null)
                    {
                        var data = (SnapshotData?)_serializer.Deserialize(entry.Value, typeof(SnapshotData));
                        if (data != null)
                        {
                            history.Add(new SnapshotInfo(data.Version, new DateTime(data.TimestampTicks, DateTimeKind.Utc)));
                        }
                    }
                }
                catch (NatsKVKeyNotFoundException)
                {
                    // Key was deleted between listing and reading, skip safely
                }
            }

            return (IReadOnlyList<SnapshotInfo>)history;
        }, ct);
    }

    public async ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            var keys = await GetSnapshotKeysAsync(streamId, ct);
            foreach (var (key, _) in keys)
            {
                try { await _kvStore!.DeleteAsync(key, cancellationToken: ct); }
                catch (NatsKVKeyNotFoundException) { /* Already deleted, ignore */ }
            }

            try { await _kvStore!.DeleteAsync($"{streamId}.latest", cancellationToken: ct); }
            catch (NatsKVKeyNotFoundException) { /* Already deleted, ignore */ }
        }, ct);
    }

    public async ValueTask DeleteBeforeVersionAsync(
        string streamId,
        long version,
        CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            var keys = await GetSnapshotKeysAsync(streamId, ct);
            foreach (var (key, v) in keys.Where(k => k.version < version))
            {
                try { await _kvStore!.DeleteAsync(key, cancellationToken: ct); }
                catch (NatsKVKeyNotFoundException) { /* Already deleted, ignore */ }
            }
        }, ct);
    }

    public async ValueTask CleanupAsync(
        string streamId,
        int keepCount,
        CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            var keys = await GetSnapshotKeysAsync(streamId, ct);
            var toDelete = keys.OrderByDescending(k => k.version).Skip(keepCount);

            foreach (var (key, _) in toDelete)
            {
                try { await _kvStore!.DeleteAsync(key, cancellationToken: ct); }
                catch (NatsKVKeyNotFoundException) { /* Already deleted, ignore */ }
            }
        }, ct);
    }

    private async ValueTask<List<(string key, long version)>> GetSnapshotKeysAsync(string streamId, CancellationToken ct)
    {
        var keys = new List<(string key, long version)>();
        var prefix = $"{streamId}.v";

        await foreach (var key in _kvStore!.GetKeysAsync(cancellationToken: ct))
        {
            if (key.StartsWith(prefix))
            {
                var versionStr = key[prefix.Length..];
                if (long.TryParse(versionStr, out var version))
                {
                    keys.Add((key, version));
                }
            }
        }

        return keys;
    }

    private sealed class SnapshotData
    {
        public long Version { get; set; }
        public long TimestampTicks { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public byte[] Data { get; set; } = [];
    }
}
