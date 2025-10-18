using Catga.EventSourcing;
using Catga.Messages;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Catga.Persistence;

/// <summary>
/// NATS JetStream-based event store for persistent event sourcing
/// Uses JetStream streams instead of KV for better compatibility
/// </summary>
public sealed class NatsJSEventStore : IEventStore, IAsyncDisposable
{
    private readonly INatsConnection _connection;
    private readonly INatsJSContext _jetStream;
    private readonly string _streamName;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public NatsJSEventStore(
        INatsConnection connection,
        string? streamName = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _streamName = streamName ?? "CATGA_EVENTS";
        _jetStream = new NatsJSContext(_connection);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            // Create stream for events
            var config = new StreamConfig(
                _streamName,
                new[] { $"{_streamName}.>" }
            )
            {
                Storage = StreamConfigStorage.File,
                Retention = StreamConfigRetention.Limits,
                MaxAge = TimeSpan.FromDays(365) // Keep events for 1 year
            };

            try
            {
                await _jetStream.CreateStreamAsync(config, cancellationToken);
            }
            catch (NatsJSApiException ex) when (ex.Error.Code == 400)
            {
                // Stream already exists, ignore
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

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

        // Publish events to JetStream
        var subject = $"{_streamName}.{streamId}";
        foreach (var @event in events)
        {
            var data = SerializeEvent(@event);
            var ack = await _jetStream.PublishAsync(subject, data, cancellationToken: cancellationToken);

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

        var subject = $"{_streamName}.{streamId}";
        var storedEvents = new List<StoredEvent>();

        try
        {
            // Create temporary consumer
            var consumerName = $"temp-{streamId}-{Guid.NewGuid():N}";
            var consumer = await _jetStream.CreateOrUpdateConsumerAsync(
                _streamName,
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
                    var @event = DeserializeEvent(msg.Data);
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

        var subject = $"{_streamName}.{streamId}";

        try
        {
            // Create temporary consumer to get last message
            var consumerName = $"ver-{streamId}-{Guid.NewGuid():N}";
            var consumer = await _jetStream.CreateOrUpdateConsumerAsync(
                _streamName,
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] SerializeEvent(IEvent @event)
    {
        var envelope = new EventEnvelope
        {
            EventType = @event.GetType().AssemblyQualifiedName!,
            Data = JsonSerializer.Serialize(@event, @event.GetType())
        };
        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IEvent DeserializeEvent(byte[] data)
    {
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(data);
        if (envelope == null)
            throw new InvalidOperationException("Failed to deserialize event envelope");

        var eventType = Type.GetType(envelope.EventType)
            ?? throw new InvalidOperationException($"Event type not found: {envelope.EventType}");

        var @event = JsonSerializer.Deserialize(envelope.Data, eventType) as IEvent
            ?? throw new InvalidOperationException($"Failed to deserialize event: {envelope.EventType}");

        return @event;
    }

    private sealed class EventEnvelope
    {
        public string EventType { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }

    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

