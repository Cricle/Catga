using Catga.Abstractions;
using Catga.EventSourcing;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;

namespace Catga.Persistence.Nats.Stores;

/// <summary>
/// NATS JetStream-based audit log store.
/// </summary>
public sealed class NatsJSAuditLogStore : IAuditLogStore
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly string _streamName;
    private INatsJSContext? _js;
    private INatsJSStream? _stream;

    public NatsJSAuditLogStore(
        INatsConnection nats,
        IMessageSerializer serializer,
        IOptions<NatsJSStoreOptions> options)
    {
        _nats = nats;
        _serializer = serializer;
        _streamName = options.Value.StreamName + "_AUDIT";
    }

    private async ValueTask EnsureStreamAsync(CancellationToken ct)
    {
        if (_stream != null) return;

        _js = new NatsJSContext(_nats);
        try
        {
            _stream = await _js.GetStreamAsync(_streamName, cancellationToken: ct);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            var config = new StreamConfig(_streamName, [$"{_streamName}.>"])
            {
                Storage = StreamConfigStorage.File,
                Retention = StreamConfigRetention.Limits,
                MaxAge = TimeSpan.FromDays(365),
                Discard = StreamConfigDiscard.Old
            };
            _stream = await _js.CreateStreamAsync(config, ct);
        }
    }

    public async ValueTask LogAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        await EnsureStreamAsync(ct);
        var subject = $"{_streamName}.{entry.StreamId}.{entry.Timestamp.Ticks}";
        var data = _serializer.Serialize(entry);
        await _js!.PublishAsync(subject, data, cancellationToken: ct);
    }

    public async ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsAsync(string streamId, CancellationToken ct = default)
    {
        await EnsureStreamAsync(ct);
        var subject = $"{_streamName}.{streamId}.>";
        return await FetchEntriesAsync(subject, ct);
    }

    public async ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsByTimeRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await EnsureStreamAsync(ct);
        var entries = await FetchEntriesAsync($"{_streamName}.>", ct);
        return entries.Where(e => e.Timestamp >= from && e.Timestamp <= to).OrderBy(e => e.Timestamp).ToList();
    }

    public async ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsByUserAsync(string userId, CancellationToken ct = default)
    {
        await EnsureStreamAsync(ct);
        var entries = await FetchEntriesAsync($"{_streamName}.>", ct);
        return entries.Where(e => e.UserId == userId).OrderBy(e => e.Timestamp).ToList();
    }

    private async Task<List<AuditLogEntry>> FetchEntriesAsync(string subject, CancellationToken ct)
    {
        var entries = new List<AuditLogEntry>();
        var consumer = await _js!.CreateOrUpdateConsumerAsync(_streamName, new ConsumerConfig
        {
            FilterSubject = subject,
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.None
        }, ct);

        await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = 10000 }, cancellationToken: ct))
        {
            if (msg.Data != null)
            {
                var entry = _serializer.Deserialize<AuditLogEntry>(msg.Data);
                if (entry != null) entries.Add(entry);
            }
        }

        return entries.OrderBy(e => e.Timestamp).ToList();
    }
}

/// <summary>
/// NATS KV-based GDPR store.
/// </summary>
public sealed class NatsJSGdprStore : IGdprStore
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly string _bucketName;
    private INatsKVContext? _kv;
    private INatsKVStore? _store;

    public NatsJSGdprStore(
        INatsConnection nats,
        IMessageSerializer serializer,
        IOptions<NatsJSStoreOptions> options)
    {
        _nats = nats;
        _serializer = serializer;
        _bucketName = options.Value.StreamName.ToLowerInvariant() + "_gdpr";
    }

    private async ValueTask EnsureStoreAsync(CancellationToken ct)
    {
        if (_store != null) return;

        _kv = new NatsKVContext(new NatsJSContext(_nats));
        try
        {
            _store = await _kv.GetStoreAsync(_bucketName, ct);
        }
        catch
        {
            _store = await _kv.CreateStoreAsync(new NatsKVConfig(_bucketName)
            {
                History = 1,
                Storage = NatsKVStorageType.File
            }, ct);
        }
    }

    public async ValueTask SaveRequestAsync(ErasureRequest request, CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);
        var data = _serializer.Serialize(request);
        await _store!.PutAsync(request.SubjectId, data, cancellationToken: ct);
    }

    public async ValueTask<ErasureRequest?> GetErasureRequestAsync(string subjectId, CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);
        try
        {
            var entry = await _store!.GetEntryAsync<byte[]>(subjectId, cancellationToken: ct);
            return entry.Value != null ? _serializer.Deserialize<ErasureRequest>(entry.Value) : null;
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask<IReadOnlyList<ErasureRequest>> GetPendingRequestsAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);
        var requests = new List<ErasureRequest>();

        await foreach (var key in _store!.GetKeysAsync(cancellationToken: ct))
        {
            var request = await GetErasureRequestAsync(key, ct);
            if (request?.Status == ErasureStatus.Pending)
            {
                requests.Add(request);
            }
        }

        return requests;
    }
}

/// <summary>
/// NATS KV-based encryption key store.
/// </summary>
public sealed class NatsJSEncryptionKeyStore : IEncryptionKeyStore
{
    private readonly INatsConnection _nats;
    private readonly string _bucketName;
    private INatsKVContext? _kv;
    private INatsKVStore? _store;

    public NatsJSEncryptionKeyStore(
        INatsConnection nats,
        IOptions<NatsJSStoreOptions> options)
    {
        _nats = nats;
        _bucketName = options.Value.StreamName.ToLowerInvariant() + "_enckeys";
    }

    private async ValueTask EnsureStoreAsync(CancellationToken ct)
    {
        if (_store != null) return;

        _kv = new NatsKVContext(new NatsJSContext(_nats));
        try
        {
            _store = await _kv.GetStoreAsync(_bucketName, ct);
        }
        catch
        {
            _store = await _kv.CreateStoreAsync(new NatsKVConfig(_bucketName)
            {
                History = 1,
                Storage = NatsKVStorageType.File
            }, ct);
        }
    }

    public async ValueTask SaveKeyAsync(string subjectId, byte[] key, CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);
        await _store!.PutAsync(subjectId, key, cancellationToken: ct);
    }

    public async ValueTask<byte[]?> GetKeyAsync(string subjectId, CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);
        try
        {
            var entry = await _store!.GetEntryAsync<byte[]>(subjectId, cancellationToken: ct);
            return entry.Value;
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DeleteKeyAsync(string subjectId, CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);
        try
        {
            await _store!.DeleteAsync(subjectId, cancellationToken: ct);
        }
        catch
        {
            // Key doesn't exist, ignore
        }
    }
}
