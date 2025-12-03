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

    protected override string[] GetSubjects() => new[] { $"{StreamName}.>" };

    public async ValueTask AppendAsync(
        string streamId,
        IReadOnlyList<IEvent> events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        await provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.EventStore.Append", ActivityKind.Producer);
            try
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
                ArgumentNullException.ThrowIfNull(events);

                await EnsureInitializedAsync(ct);

                if (events.Count == 0) return;

                // Get current version for optimistic concurrency
                var currentVersion = await GetVersionAsync(streamId, ct);

                if (expectedVersion != -1 && currentVersion != expectedVersion)
                {
                    var ex = new ConcurrencyException(streamId, expectedVersion, currentVersion);
                    activity?.AddActivityEvent(CatgaActivitySource.Events.EventStoreAppendConcurrencyMismatch,
                        ("stream", streamId),
                        ("expected", expectedVersion),
                        ("current", currentVersion));
                    activity?.SetError(ex);
                    throw ex;
                }

                // Publish events to JetStream with type information in headers
                var subject = $"{StreamName}.{streamId}";
                foreach (var @event in events)
                {
                    var runtimeType = GetRuntimeTypeForSerialization(@event);
                    var typeFull = runtimeType.AssemblyQualifiedName ?? runtimeType.FullName!;
                    _registry.Register(typeFull, runtimeType);
                    var resolvedType = _registry.Resolve(typeFull)!;
                    var data = serializer.Serialize(@event, resolvedType);
                    var headers = new NatsHeaders
                    {
                        ["EventType"] = typeFull
                    };

                    var ack = await JetStream.PublishAsync(subject, data, headers: headers, cancellationToken: ct);

                    if (ack.Error != null)
                    {
                        var ex = new InvalidOperationException($"Failed to publish event: {ack.Error.Description}");
                        activity?.SetError(ex);
                        throw ex;
                    }
                    activity?.AddActivityEvent(CatgaActivitySource.Events.EventStoreAppendItem,
                        ("stream", streamId),
                        ("event.type", resolvedType.Name),
                        ("seq", (long)ack.Seq),
                        ("dup", ack.Duplicate));
                }
                CatgaDiagnostics.EventStoreAppends.Add(events.Count);
                CatgaDiagnostics.EventStoreAppendDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                activity?.AddActivityEvent(CatgaActivitySource.Events.EventStoreAppendDone,
                    ("stream", streamId),
                    ("count", events.Count));
            }
            catch (Exception ex)
            {
                activity?.SetError(ex);
                throw;
            }
        }, cancellationToken);
    }

    public async ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
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
                // Create temporary consumer
                var consumerName = $"temp-{streamId}-{Guid.NewGuid():N}";
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                    StreamName,
                    new ConsumerConfig
                    {
                        Name = consumerName,
                        FilterSubject = subject,
                        AckPolicy = ConsumerConfigAckPolicy.None,
                        DeliverPolicy = ConsumerConfigDeliverPolicy.All
                    },
                    ct);

                // Fetch events
                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    new NatsJSFetchOpts { MaxMsgs = maxCount },
                    cancellationToken: ct))
                {
                    if (msg.Data != null && msg.Data.Length > 0)
                    {
                        var deserStart = Stopwatch.GetTimestamp();
                        var @event = DeserializeEventFromMessage(msg);
                        var deserMs = (Stopwatch.GetTimestamp() - deserStart) * 1000.0 / Stopwatch.Frequency;
                        activity?.AddActivityEvent(CatgaActivitySource.Events.EventStoreReadDeserialized,
                            ("stream", streamId),
                            ("event.type", @event.GetType().Name),
                            ("duration.ms", deserMs),
                            ("payload.size", msg.Data.Length));
                        var version = (long)(msg.Metadata?.Sequence.Stream ?? 0UL) - 1; // Convert to 0-based

                        if (version >= fromVersion)
                        {
                            storedEvents.Add(new StoredEvent
                            {
                                Version = version,
                                Event = @event,
                                Timestamp = msg.Metadata?.Timestamp.UtcDateTime ?? DateTime.UtcNow,
                                EventType = @event.GetType().Name
                            });
                            activity?.AddActivityEvent(CatgaActivitySource.Events.EventStoreReadItem,
                                ("stream", streamId),
                                ("version", version));
                        }

                        if (storedEvents.Count >= maxCount) break;
                    }
                }
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                // Stream doesn't exist yet
                return new EventStream
                {
                    StreamId = streamId,
                    Version = -1,
                    Events = Array.Empty<StoredEvent>()
                };
            }
            catch (Exception ex)
            {
                activity?.SetError(ex);
                throw;
            }

            var finalVersion = storedEvents.Count > 0 ? storedEvents[^1].Version : -1;

            var result = new EventStream
            {
                StreamId = streamId,
                Version = finalVersion,
                Events = storedEvents
            };
            CatgaDiagnostics.EventStoreReads.Add(1);
            CatgaDiagnostics.EventStoreReadDuration.Record((double)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency));
            activity?.AddActivityEvent(CatgaActivitySource.Events.EventStoreReadDone,
                ("stream", streamId),
                ("count", storedEvents.Count));
            return result;
        }, cancellationToken);
    }

    public async ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        return await provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.EventStore.GetVersion", ActivityKind.Internal);
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

            await EnsureInitializedAsync(ct);

            var subject = $"{StreamName}.{streamId}";

            try
            {
                // Create temporary consumer to get last message
                var consumerName = $"ver-{streamId}-{Guid.NewGuid():N}";
                var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                    StreamName,
                    new ConsumerConfig
                    {
                        Name = consumerName,
                        FilterSubject = subject,
                        AckPolicy = ConsumerConfigAckPolicy.None,
                        DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject
                    },
                    ct);

                await foreach (var msg in consumer.FetchAsync<byte[]>(
                    new NatsJSFetchOpts { MaxMsgs = 1 },
                    cancellationToken: ct))
                {
                    return (long)(msg.Metadata?.Sequence.Stream ?? 0UL) - 1; // Convert to 0-based
                }

                var verNone = -1; // No messages found
                CatgaDiagnostics.EventStoreReads.Add(1);
                var elapsedNone = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                CatgaDiagnostics.EventStoreReadDuration.Record(elapsedNone);
                activity?.AddActivityEvent(CatgaActivitySource.Events.EventStoreGetVersionNone,
                    ("stream", streamId));
                return verNone;
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 404)
            {
                var ver = -1; // Stream doesn't exist
                CatgaDiagnostics.EventStoreReads.Add(1);
                var elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                CatgaDiagnostics.EventStoreReadDuration.Record(elapsed);
                activity?.AddActivityEvent(CatgaActivitySource.Events.EventStoreGetVersionNotFound,
                    ("stream", streamId));
                return ver;
            }
        }, cancellationToken);
    }

    private IEvent DeserializeEventFromMessage(NatsJSMsg<byte[]> msg)
    {
        // 从 headers 获取事件类型（优先使用 AssemblyQualifiedName）
        var eventTypeName = msg.Headers?["EventType"];
        if (string.IsNullOrEmpty(eventTypeName))
            throw new InvalidOperationException("Event type not found in message headers");

        var t = _registry.Resolve(eventTypeName!)
            ?? throw new InvalidOperationException($"Unknown event type: {eventTypeName}. Ensure it is preserved during trimming or registered in the event type registry.");
        return (IEvent?)serializer.Deserialize(msg.Data!, t) ?? throw new InvalidOperationException($"Fail to deserialize type {t}");
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private static Type GetEventTypeFromHeader(string typeName)
    {
        throw new UnreachableException("This overload should not be called");
    }

    private static Type GetRuntimeTypeForSerialization(object instance)
        => instance.GetType();
}
