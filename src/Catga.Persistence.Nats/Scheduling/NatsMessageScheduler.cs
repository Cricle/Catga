using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Abstractions;
using Catga.Scheduling;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace Catga.Persistence.Nats.Scheduling;

/// <summary>
/// NATS KV-based message scheduler for delayed message delivery.
/// </summary>
public sealed class NatsMessageScheduler : IMessageScheduler
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly ICatgaMediator _mediator;
    private readonly MessageSchedulerOptions _options;
    private readonly string _bucketName;
    private INatsKVContext? _kv;
    private INatsKVStore? _store;

    public NatsMessageScheduler(
        INatsConnection nats,
        IMessageSerializer serializer,
        ICatgaMediator mediator,
        IOptions<MessageSchedulerOptions> options,
        string bucketName = "scheduler")
    {
        _nats = nats;
        _serializer = serializer;
        _mediator = mediator;
        _options = options.Value;
        _bucketName = bucketName;
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

    public async ValueTask<ScheduledMessageHandle> ScheduleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        DateTimeOffset deliverAt,
        CancellationToken ct = default) where TMessage : class, IMessage
    {
        await EnsureStoreAsync(ct);

        var scheduleId = Guid.NewGuid().ToString("N");
        var messageType = typeof(TMessage).FullName ?? typeof(TMessage).Name;
        var payload = _serializer.Serialize(message);

        var entry = new ScheduledEntry
        {
            ScheduleId = scheduleId,
            MessageType = messageType,
            Payload = payload,
            DeliverAt = deliverAt.UtcTicks,
            CreatedAt = DateTimeOffset.UtcNow.UtcTicks,
            Status = (int)ScheduledMessageStatus.Pending,
            RetryCount = 0
        };

        var data = _serializer.Serialize(entry);
        var key = $"{_options.KeyPrefix}{deliverAt.UtcTicks:D20}:{scheduleId}";

        await _store!.PutAsync(key, data, cancellationToken: ct);

        return new ScheduledMessageHandle
        {
            ScheduleId = scheduleId,
            DeliverAt = deliverAt,
            MessageType = messageType
        };
    }

    public ValueTask<ScheduledMessageHandle> ScheduleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        TMessage message,
        TimeSpan delay,
        CancellationToken ct = default) where TMessage : class, IMessage
    {
        return ScheduleAsync(message, DateTimeOffset.UtcNow.Add(delay), ct);
    }

    public async ValueTask<bool> CancelAsync(string scheduleId, CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        await foreach (var key in _store!.GetKeysAsync(cancellationToken: ct))
        {
            if (key.EndsWith(scheduleId))
            {
                try
                {
                    var entry = await _store.GetEntryAsync<byte[]>(key, cancellationToken: ct);
                    if (entry.Value != null)
                    {
                        var scheduled = _serializer.Deserialize<ScheduledEntry>(entry.Value);
                        if (scheduled != null && scheduled.Status == (int)ScheduledMessageStatus.Pending)
                        {
                            scheduled.Status = (int)ScheduledMessageStatus.Cancelled;
                            await _store.UpdateAsync(key, _serializer.Serialize(scheduled), entry.Revision, cancellationToken: ct);
                            return true;
                        }
                    }
                }
                catch
                {
                    // Ignore errors
                }
            }
        }

        return false;
    }

    public async ValueTask<ScheduledMessageInfo?> GetAsync(string scheduleId, CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        await foreach (var key in _store!.GetKeysAsync(cancellationToken: ct))
        {
            if (key.EndsWith(scheduleId))
            {
                try
                {
                    var entry = await _store.GetEntryAsync<byte[]>(key, cancellationToken: ct);
                    if (entry.Value != null)
                    {
                        var scheduled = _serializer.Deserialize<ScheduledEntry>(entry.Value);
                        if (scheduled != null)
                        {
                            return ToInfo(scheduled);
                        }
                    }
                }
                catch
                {
                    // Ignore errors
                }
            }
        }

        return null;
    }

    public async IAsyncEnumerable<ScheduledMessageInfo> ListPendingAsync(
        int limit = 100,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var count = 0;
        await foreach (var key in _store!.GetKeysAsync(cancellationToken: ct))
        {
            if (count >= limit) break;

            ScheduledMessageInfo? info = null;
            try
            {
                var entry = await _store.GetEntryAsync<byte[]>(key, cancellationToken: ct);
                if (entry.Value != null)
                {
                    var scheduled = _serializer.Deserialize<ScheduledEntry>(entry.Value);
                    if (scheduled != null && scheduled.Status == (int)ScheduledMessageStatus.Pending)
                    {
                        info = ToInfo(scheduled);
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            if (info.HasValue)
            {
                yield return info.Value;
                count++;
            }
        }
    }

    private static ScheduledMessageInfo ToInfo(ScheduledEntry entry)
    {
        return new ScheduledMessageInfo
        {
            ScheduleId = entry.ScheduleId,
            MessageType = entry.MessageType,
            DeliverAt = new DateTimeOffset(entry.DeliverAt, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(entry.CreatedAt, TimeSpan.Zero),
            Status = (ScheduledMessageStatus)entry.Status,
            RetryCount = entry.RetryCount,
            LastError = entry.LastError
        };
    }

    private sealed class ScheduledEntry
    {
        public string ScheduleId { get; set; } = "";
        public string MessageType { get; set; } = "";
        public byte[] Payload { get; set; } = [];
        public long DeliverAt { get; set; }
        public long CreatedAt { get; set; }
        public int Status { get; set; }
        public int RetryCount { get; set; }
        public string? LastError { get; set; }
    }
}
