using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.EventSourcing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Client.JetStream;

namespace Catga.Persistence.Nats.Stores;

/// <summary>NATS KV-based snapshot store for event sourcing.</summary>
public sealed partial class NatsSnapshotStore : ISnapshotStore
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly SnapshotOptions _opts;
    private readonly ILogger<NatsSnapshotStore> _logger;
    private volatile INatsKVStore? _store;
    private Task? _initTask;

    public NatsSnapshotStore(
        INatsConnection nats,
        IMessageSerializer serializer,
        IOptions<SnapshotOptions> options,
        ILogger<NatsSnapshotStore> logger)
    {
        _nats = nats;
        _serializer = serializer;
        _opts = options.Value;
        _logger = logger;
    }

    private static string EncodeKey(string id) => id.Replace(":", "_C_").Replace("/", "_S_").Replace(".", "_D_");

    private async ValueTask EnsureInitializedAsync(CancellationToken ct)
    {
        if (_store != null) return;

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
                _store = await kv.CreateStoreAsync(new NatsKVConfig("snapshots")
                {
                    History = 1,
                    Storage = NatsKVStorageType.File,
                    MaxAge = TimeSpan.FromDays(30)
                }, ct);
            }
            catch (NatsKVException)
            {
                // Store already exists, get it
                _store = await kv.GetStoreAsync("snapshots", ct);
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
        string streamId, TAggregate aggregate, long version, CancellationToken ct = default)
        where TAggregate : class
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(_opts.KeyPrefix + streamId);
        var stored = new StoredSnapshot
        {
            StreamId = streamId,
            Version = version,
            Timestamp = DateTime.UtcNow,
            AggregateType = typeof(TAggregate).AssemblyQualifiedName ?? typeof(TAggregate).Name,
            State = _serializer.Serialize(aggregate)
        };
        var data = _serializer.Serialize(stored);

        try
        {
            await _store!.CreateAsync(key, data, cancellationToken: ct);
        }
        catch (NatsKVCreateException)
        {
            // Already exists, update it
            var entry = await _store!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
            await _store!.UpdateAsync(key, data, entry.Revision, cancellationToken: ct);
        }

        LogSnapshotSaved(_logger, streamId, version);
    }

    public async ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId, CancellationToken ct = default)
        where TAggregate : class
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(_opts.KeyPrefix + streamId);
        try
        {
            var entry = await _store!.GetEntryAsync<byte[]>(key, cancellationToken: ct);
            if (entry.Value == null) return null;

            var stored = _serializer.Deserialize<StoredSnapshot>(entry.Value);
            if (stored == null || stored.State == null) return null;

            var state = _serializer.Deserialize<TAggregate>(stored.State);
            if (state == null) return null;

            LogSnapshotLoaded(_logger, streamId, stored.Version);
            return new Snapshot<TAggregate>
            {
                StreamId = streamId,
                State = state,
                Version = stored.Version,
                Timestamp = stored.Timestamp
            };
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
    }

    public async ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var key = EncodeKey(_opts.KeyPrefix + streamId);
        try
        {
            await _store!.DeleteAsync(key, cancellationToken: ct);
            LogSnapshotDeleted(_logger, streamId);
        }
        catch (NatsKVKeyNotFoundException) { /* already deleted */ }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot saved: {StreamId} at version {Version}")]
    private static partial void LogSnapshotSaved(ILogger logger, string streamId, long version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot loaded: {StreamId} at version {Version}")]
    private static partial void LogSnapshotLoaded(ILogger logger, string streamId, long version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot deleted: {StreamId}")]
    private static partial void LogSnapshotDeleted(ILogger logger, string streamId);
}

/// <summary>Internal storage format for snapshots.</summary>
public sealed class StoredSnapshot
{
    public string StreamId { get; set; } = "";
    public long Version { get; set; }
    public DateTime Timestamp { get; set; }
    public string AggregateType { get; set; } = "";
    public byte[]? State { get; set; }
}
