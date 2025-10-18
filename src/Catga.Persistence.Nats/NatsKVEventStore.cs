using Catga.EventSourcing;
using Catga.Messages;
using Catga.Serialization;
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
        string? streamName = null)
        : base(connection, streamName ?? "CATGA_EVENTS")
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    protected override StreamConfig CreateStreamConfig() => new(
        StreamName,
        new[] { $"{StreamName}.>" }
    )
    {
        Storage = StreamConfigStorage.File,
        Retention = StreamConfigRetention.Limits,
        MaxAge = TimeSpan.FromDays(365) // Keep events for 1 year
    };

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
                ["EventType"] = @event.GetType().AssemblyQualifiedName!
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
        return _serializer.Serialize(@event);
    }

    /// <summary>
    /// 从 NATS 消息反序列化事件（从 headers 读取类型信息）
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2057:Unrecognized value passed to parameter.", Justification = "Event type is stored in NATS headers at runtime. For AOT scenarios, use strongly-typed queries.")]
    private IEvent DeserializeEventFromMessage(NatsJSMsg<byte[]> msg)
    {
        // 从 headers 获取事件类型
        var eventTypeName = msg.Headers?["EventType"];
        if (string.IsNullOrEmpty(eventTypeName))
            throw new InvalidOperationException("Event type not found in message headers");

        var eventType = Type.GetType(eventTypeName!)
            ?? throw new InvalidOperationException($"Event type not found: {eventTypeName}");

        return DeserializeEvent(msg.Data!, eventType);
    }

    /// <summary>
    /// 反序列化事件对象
    /// 警告：由于需要动态类型反序列化，此方法不完全 AOT 兼容
    /// 建议：使用强类型的 GetEventsAsync&lt;TEvent&gt; 方法以获得完全 AOT 支持
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [UnconditionalSuppressMessage("Trimming", "IL2057:Unrecognized value passed to parameter. It's not possible to guarantee the availability of the target.", Justification = "Event deserialization requires dynamic type loading from message headers. Use strongly-typed queries for AOT scenarios.")]
    [UnconditionalSuppressMessage("Trimming", "IL2071:Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute'.", Justification = "Event deserialization requires dynamic type loading. Use strongly-typed GetEventsAsync<TEvent> for AOT scenarios.")]
    [UnconditionalSuppressMessage("Trimming", "IL2087:Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method.", Justification = "Event deserialization requires dynamic type loading. Use strongly-typed GetEventsAsync<TEvent> for AOT scenarios.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "Event deserialization requires dynamic type loading. Use strongly-typed GetEventsAsync<TEvent> for AOT scenarios.")]
    private IEvent DeserializeEvent(byte[] data, Type eventType)
    {
        // 使用反射调用泛型方法（在 AOT 场景下可能失败）
        // 为了 AOT 支持，推荐使用强类型的 GetEventsAsync<TEvent> 方法
        var deserializeMethod = typeof(IMessageSerializer)
            .GetMethod(nameof(IMessageSerializer.Deserialize))!
            .MakeGenericMethod(eventType);

        var @event = deserializeMethod.Invoke(_serializer, new object[] { data }) as IEvent
            ?? throw new InvalidOperationException($"Failed to deserialize event of type: {eventType.FullName}");

        return @event;
    }
}

