using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace Catga.Persistence.Nats;

/// <summary>
/// NATS KV-based projection checkpoint store.
/// </summary>
public sealed class NatsProjectionCheckpointStore : IProjectionCheckpointStore
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;
    private readonly string _bucketName;
    private INatsKVContext? _kv;
    private INatsKVStore? _kvStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public NatsProjectionCheckpointStore(
        INatsConnection nats,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        string bucketName = "projection-checkpoints")
    {
        _nats = nats;
        _serializer = serializer;
        _provider = provider;
        _bucketName = bucketName;
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_kvStore != null) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_kvStore != null) return;

            _kv = new NatsKVContext(new NatsJSContext(_nats));
            try
            {
                _kvStore = await _kv.GetStoreAsync(_bucketName, ct);
            }
            catch (NatsKVException)
            {
                _kvStore = await _kv.CreateStoreAsync(new NatsKVConfig(_bucketName), ct);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask SaveAsync(ProjectionCheckpoint checkpoint, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            var data = new CheckpointData
            {
                Position = checkpoint.Position,
                LastUpdatedTicks = checkpoint.LastUpdated.Ticks,
                StreamId = checkpoint.StreamId
            };

            var bytes = _serializer.Serialize(data, typeof(CheckpointData));
            await _kvStore!.PutAsync(checkpoint.ProjectionName, bytes, cancellationToken: ct);
        }, ct);
    }

    public async ValueTask<ProjectionCheckpoint?> LoadAsync(string projectionName, CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);

            try
            {
                var entry = await _kvStore!.GetEntryAsync<byte[]>(projectionName, cancellationToken: ct);
                if (entry.Value == null) return null;

                var data = (CheckpointData?)_serializer.Deserialize(entry.Value, typeof(CheckpointData));
                if (data == null) return null;

                return new ProjectionCheckpoint
                {
                    ProjectionName = projectionName,
                    Position = data.Position,
                    LastUpdated = new DateTime(data.LastUpdatedTicks, DateTimeKind.Utc),
                    StreamId = data.StreamId
                };
            }
            catch (NatsKVKeyNotFoundException)
            {
                return null;
            }
        }, ct);
    }

    public async ValueTask DeleteAsync(string projectionName, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            await EnsureInitializedAsync(ct);
            try
            {
                await _kvStore!.DeleteAsync(projectionName, cancellationToken: ct);
            }
            catch (NatsKVKeyNotFoundException)
            {
                // Already deleted
            }
        }, ct);
    }

    private sealed class CheckpointData
    {
        public long Position { get; set; }
        public long LastUpdatedTicks { get; set; }
        public string? StreamId { get; set; }
    }
}
