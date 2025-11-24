using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Core;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Catga.Persistence;

/// <summary>
/// NATS JetStream-based event store for persistent event sourcing
/// Uses JetStream streams instead of KV for better compatibility
/// </summary>
public sealed class NatsJSEventStore : NatsJSStoreBase, IEventStore
{
    private readonly IMessageSerializer _serializer;

    public NatsJSEventStore(
        INatsConnection connection,
        IMessageSerializer serializer,
        string? streamName = null,
        NatsJSStoreOptions? options = null)
        : base(connection, streamName ?? "CATGA_EVENTS", options)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    protected override string[] GetSubjects() => new[] { $"{StreamName}.>" };

    public async ValueTask AppendAsync(
        string streamId,
        IReadOnlyList<IEvent> events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(events);

        await EnsureInitializedAsync(cancellationToken);

        if (events.Count == 0) return;

        // Get current version for optimistic concurrency
        var currentVersion = await GetVersionAsync(streamId, cancellationToken);

        if (expectedVersion != -1 && currentVersion != expectedVersion)
        {
            throw new ConcurrencyException(streamId, expectedVersion, currentVersion);
        }

        // Publish events to JetStream with type information in headers
        var subject = $"{StreamName}.{streamId}";
        foreach (var @event in events)
        {
            var data = SerializeEvent(@event);
            var headers = new NatsHeaders
            {
                ["EventType"] = @event.GetType().FullName!
            };

            var ack = await JetStream.PublishAsync(subject, data, headers: headers, cancellationToken: cancellationToken);

            if (ack.Error != null)
            {
                throw new InvalidOperationException($"Failed to publish event: {ack.Error.Description}");
            }
        }
    }

    public async ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        await EnsureInitializedAsync(cancellationToken);

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
                cancellationToken);

            // Fetch events
            await foreach (var msg in consumer.FetchAsync<byte[]>(
                new NatsJSFetchOpts { MaxMsgs = maxCount },
                cancellationToken: cancellationToken))
            {
                if (msg.Data != null && msg.Data.Length > 0)
                {
                    var @event = DeserializeEventFromMessage(msg);
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

        var finalVersion = storedEvents.Count > 0 ? storedEvents[^1].Version : -1;

        return new EventStream
        {
            StreamId = streamId,
            Version = finalVersion,
            Events = storedEvents
        };
    }

    public async ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        await EnsureInitializedAsync(cancellationToken);

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
                cancellationToken);

            await foreach (var msg in consumer.FetchAsync<byte[]>(
                new NatsJSFetchOpts { MaxMsgs = 1 },
                cancellationToken: cancellationToken))
            {
                return (long)(msg.Metadata?.Sequence.Stream ?? 0UL) - 1; // Convert to 0-based
            }

            return -1; // No messages found
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            return -1; // Stream doesn't exist
        }
    }

    /// <summary>
    /// 直接序列化事件对象，不使用 Envelope 包装
    /// 注意：事件类型信息通过 JetStream headers 传递
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte[] SerializeEvent(IEvent @event)
    {
        return _serializer.Serialize(@event, @event.GetType());
    }

    /// <summary>
    /// 从 NATS 消息反序列化事件（从 headers 读取类型信息）
    /// </summary>
    /// <remarks>
    /// 使用非泛型 Deserialize(byte[], Type) 方法避免反射调用泛型方法。
    /// Type 参数已标记 DynamicallyAccessedMembers，提供更好的 AOT 兼容性提示。
    /// </remarks>
    private IEvent DeserializeEventFromMessage(NatsJSMsg<byte[]> msg)
    {
        // 从 headers 获取事件类型
        var eventTypeName = msg.Headers?["EventType"];
        if (string.IsNullOrEmpty(eventTypeName))
            throw new InvalidOperationException("Event type not found in message headers");

        if (Catga.Generated.EventTypeRegistry.TryDeserialize(eventTypeName!, msg.Data!, _serializer, out var evt))
            return evt;

        throw new InvalidOperationException($"Unknown event type: {eventTypeName}. Ensure it is included in compilation for registration.");
    }
}

