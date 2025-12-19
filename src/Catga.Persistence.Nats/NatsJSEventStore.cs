using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.EventSourcing;
using Catga.Observability;
using Catga.Resilience;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Persistence;

/// <summary>NATS JetStream-based event store.</summary>
public sealed class NatsJSEventStore(INatsConnection connection, IMessageSerializer serializer, IResiliencePipelineProvider provider, IEventTypeRegistry? registry = null, string? streamName = null, NatsJSStoreOptions? options = null)
    : NatsJSStoreBase(connection, streamName ?? "CATGA_EVENTS", options), IEventStore
{
    private readonly IEventTypeRegistry _registry = registry ?? new DefaultEventTypeRegistry();

    protected override string[] GetSubjects() => [$"{StreamName}.>"];

    public async ValueTask AppendAsync(string streamId, IReadOnlyList<IEvent> events, long expectedVersion = -1, CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.EventStore.Append", ActivityKind.Producer);
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            ArgumentNullException.ThrowIfNull(events);
            await EnsureInitializedAsync(ct);
            if (events.Count == 0) return;

            var currentVersion = await GetVersionAsync(streamId, ct);
            if (expectedVersion != -1 && currentVersion != expectedVersion)
                throw new ConcurrencyException(streamId, expectedVersion, currentVersion);

            var subject = $"{StreamName}.{streamId}";
            foreach (var @event in events)
            {
                var runtimeType = _registry.GetPreservedType(@event);
                var typeFull = runtimeType.AssemblyQualifiedName ?? runtimeType.FullName!;
                var data = serializer.Serialize(@event, runtimeType);
                var headers = new NatsHeaders { ["EventType"] = typeFull };
                var ack = await JetStream.PublishAsync(subject, data, headers: headers, cancellationToken: ct);
                if (ack.Error != null) throw new InvalidOperationException($"Failed to publish event: {ack.Error.Description}");
            }
            CatgaDiagnostics.EventStoreAppends.Add(events.Count);
            CatgaDiagnostics.EventStoreAppendDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        }, cancellationToken);
    }

    public async ValueTask<EventStream> ReadAsync(string streamId, long fromVersion = 0, int maxCount = int.MaxValue, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.EventStore.Read", ActivityKind.Internal);
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            await EnsureInitializedAsync(ct);

            var subject = $"{StreamName}.{streamId}";
            var storedEvents = new List<StoredEvent>();

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                    new ConsumerConfig { Name = $"temp-{streamId}-{Guid.NewGuid():N}", FilterSubject = subject, AckPolicy = ConsumerConfigAckPolicy.None, DeliverPolicy = ConsumerConfigDeliverPolicy.All }, ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = maxCount }, cancellationToken: ct))
                {
                    if (msg.Data is { Length: > 0 })
                    {
                        var @event = DeserializeEventFromMessage(msg);
                        var version = (long)(msg.Metadata?.Sequence.Stream ?? 0UL) - 1;
                        if (version >= fromVersion)
                        {
                            storedEvents.Add(new StoredEvent { Version = version, Event = @event, Timestamp = msg.Metadata?.Timestamp.UtcDateTime ?? DateTime.UtcNow, EventType = @event.GetType().Name });
                            if (storedEvents.Count >= maxCount) break;
                        }
                    }
                }
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                return new EventStream { StreamId = streamId, Version = -1, Events = [] };
            }

            CatgaDiagnostics.EventStoreReads.Add(1);
            CatgaDiagnostics.EventStoreReadDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            return new EventStream { StreamId = streamId, Version = storedEvents.Count > 0 ? storedEvents[^1].Version : -1, Events = storedEvents };
        }, cancellationToken);
    }

    public async ValueTask<long> GetVersionAsync(string streamId, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            await EnsureInitializedAsync(ct);
            var subject = $"{StreamName}.{streamId}";

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                    new ConsumerConfig { Name = $"ver-{streamId}-{Guid.NewGuid():N}", FilterSubject = subject, AckPolicy = ConsumerConfigAckPolicy.None, DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject }, ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = 1 }, cancellationToken: ct))
                    return (long)(msg.Metadata?.Sequence.Stream ?? 0UL) - 1;

                CatgaDiagnostics.EventStoreReads.Add(1);
                CatgaDiagnostics.EventStoreReadDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                return -1L;
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                CatgaDiagnostics.EventStoreReads.Add(1);
                CatgaDiagnostics.EventStoreReadDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                return -1L;
            }
        }, cancellationToken);
    }

    private IEvent DeserializeEventFromMessage(NatsJSMsg<byte[]> msg)
    {
        var eventTypeName = msg.Headers?["EventType"];
        if (string.IsNullOrEmpty(eventTypeName)) throw new InvalidOperationException("Event type not found in message headers");
        var t = _registry.Resolve(eventTypeName!) ?? throw new InvalidOperationException($"Unknown event type: {eventTypeName}");
        return (IEvent?)serializer.Deserialize(msg.Data!, t) ?? throw new InvalidOperationException($"Fail to deserialize type {t}");
    }

    public async ValueTask<EventStream> ReadToVersionAsync(string streamId, long toVersion, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            await EnsureInitializedAsync(ct);
            var subject = $"{StreamName}.{streamId}";
            var storedEvents = new List<StoredEvent>();

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                    new ConsumerConfig { Name = $"ttv-{streamId}-{Guid.NewGuid():N}", FilterSubject = subject, AckPolicy = ConsumerConfigAckPolicy.None, DeliverPolicy = ConsumerConfigDeliverPolicy.All }, ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = (int)(toVersion + 1) }, cancellationToken: ct))
                {
                    if (msg.Data is { Length: > 0 })
                    {
                        var @event = DeserializeEventFromMessage(msg);
                        var version = (long)(msg.Metadata?.Sequence.Stream ?? 0UL) - 1;
                        if (version > toVersion) break;
                        storedEvents.Add(new StoredEvent { Version = version, Event = @event, Timestamp = msg.Metadata?.Timestamp.UtcDateTime ?? DateTime.UtcNow, EventType = @event.GetType().Name });
                    }
                }
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                return new EventStream { StreamId = streamId, Version = -1, Events = [] };
            }

            CatgaDiagnostics.EventStoreReads.Add(1);
            CatgaDiagnostics.EventStoreReadDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            return new EventStream { StreamId = streamId, Version = storedEvents.Count > 0 ? storedEvents[^1].Version : -1, Events = storedEvents };
        }, cancellationToken);
    }

    public async ValueTask<EventStream> ReadToTimestampAsync(string streamId, DateTime upperBound, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            await EnsureInitializedAsync(ct);
            var subject = $"{StreamName}.{streamId}";
            var storedEvents = new List<StoredEvent>();

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                    new ConsumerConfig { Name = $"ttt-{streamId}-{Guid.NewGuid():N}", FilterSubject = subject, AckPolicy = ConsumerConfigAckPolicy.None, DeliverPolicy = ConsumerConfigDeliverPolicy.All }, ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = int.MaxValue }, cancellationToken: ct))
                {
                    if (msg.Data is { Length: > 0 })
                    {
                        var timestamp = msg.Metadata?.Timestamp.UtcDateTime ?? DateTime.UtcNow;
                        if (timestamp > upperBound) break;
                        var @event = DeserializeEventFromMessage(msg);
                        var version = (long)(msg.Metadata?.Sequence.Stream ?? 0UL) - 1;
                        storedEvents.Add(new StoredEvent { Version = version, Event = @event, Timestamp = timestamp, EventType = @event.GetType().Name });
                    }
                }
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                return new EventStream { StreamId = streamId, Version = -1, Events = [] };
            }

            CatgaDiagnostics.EventStoreReads.Add(1);
            CatgaDiagnostics.EventStoreReadDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            return new EventStream { StreamId = streamId, Version = storedEvents.Count > 0 ? storedEvents[^1].Version : -1, Events = storedEvents };
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistoryAsync(string streamId, CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            await EnsureInitializedAsync(ct);
            var subject = $"{StreamName}.{streamId}";
            var history = new List<VersionInfo>();

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                    new ConsumerConfig { Name = $"hist-{streamId}-{Guid.NewGuid():N}", FilterSubject = subject, AckPolicy = ConsumerConfigAckPolicy.None, DeliverPolicy = ConsumerConfigDeliverPolicy.All }, ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = int.MaxValue }, cancellationToken: ct))
                {
                    if (msg.Data is { Length: > 0 })
                    {
                        var version = (long)(msg.Metadata?.Sequence.Stream ?? 0UL) - 1;
                        var timestamp = msg.Metadata?.Timestamp.UtcDateTime ?? DateTime.UtcNow;
                        var eventTypeName = msg.Headers?["EventType"].ToString() ?? "Unknown";
                        history.Add(new VersionInfo { Version = version, Timestamp = timestamp, EventType = eventTypeName.Contains('.') ? eventTypeName[(eventTypeName.LastIndexOf('.') + 1)..] : eventTypeName });
                    }
                }
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                return (IReadOnlyList<VersionInfo>)[];
            }
            return (IReadOnlyList<VersionInfo>)history;
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<string>> GetAllStreamIdsAsync(CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            await EnsureInitializedAsync(ct);
            var streamIds = new HashSet<string>();

            try
            {
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(StreamName,
                    new ConsumerConfig { Name = $"proj-scan-{Guid.NewGuid():N}", AckPolicy = ConsumerConfigAckPolicy.None, DeliverPolicy = ConsumerConfigDeliverPolicy.All }, ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(new NatsJSFetchOpts { MaxMsgs = 10000 }, cancellationToken: ct))
                {
                    var subject = msg.Subject;
                    if (subject.StartsWith(StreamName + ".")) streamIds.Add(subject[(StreamName.Length + 1)..]);
                }
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                return (IReadOnlyList<string>)[];
            }
            return (IReadOnlyList<string>)streamIds.ToList();
        }, cancellationToken);
    }
}
