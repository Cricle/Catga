using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace Catga.Persistence.Nats;

/// <summary>NATS KV-based projection checkpoint store.</summary>
public sealed class NatsProjectionCheckpointStore(INatsConnection nats, IMessageSerializer serializer, IResiliencePipelineProvider provider, string bucketName = "projection-checkpoints") : IProjectionCheckpointStore
{
    private INatsKVStore? _kvStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_kvStore != null) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_kvStore != null) return;
            var kv = new NatsKVContext(new NatsJSContext(nats));
            try { _kvStore = await kv.GetStoreAsync(bucketName, ct); }
            catch (NatsKVException) { _kvStore = await kv.CreateStoreAsync(new NatsKVConfig(bucketName), ct); }
        }
        finally { _initLock.Release(); }
    }

    public async ValueTask SaveAsync(ProjectionCheckpoint checkpoint, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            var bytes = serializer.Serialize(new CheckpointData { Position = checkpoint.Position, LastUpdatedTicks = checkpoint.LastUpdated.Ticks, StreamId = checkpoint.StreamId }, typeof(CheckpointData));
            await _kvStore!.PutAsync(checkpoint.ProjectionName, bytes, cancellationToken: ct);
        }, ct);

    public async ValueTask<ProjectionCheckpoint?> LoadAsync(string projectionName, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            try
            {
                var entry = await _kvStore!.GetEntryAsync<byte[]>(projectionName, cancellationToken: ct);
                if (entry.Value == null) return null;
                var data = (CheckpointData?)serializer.Deserialize(entry.Value, typeof(CheckpointData));
                return data == null ? null : new ProjectionCheckpoint { ProjectionName = projectionName, Position = data.Position, LastUpdated = new(data.LastUpdatedTicks, DateTimeKind.Utc), StreamId = data.StreamId };
            }
            catch (NatsKVKeyNotFoundException) { return null; }
        }, ct);

    public async ValueTask DeleteAsync(string projectionName, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            try { await _kvStore!.DeleteAsync(projectionName, cancellationToken: ct); } catch (NatsKVKeyNotFoundException) { }
        }, ct);

    private sealed class CheckpointData { public long Position { get; set; } public long LastUpdatedTicks { get; set; } public string? StreamId { get; set; } }
}
