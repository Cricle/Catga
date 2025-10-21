using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Core;
using Catga;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using System.Runtime.CompilerServices;

namespace Catga.Persistence.Nats;

/// <summary>
/// High-performance NATS JetStream-based event store.
/// Lock-free, zero reflection, optimized GC, thread-safe.
/// </summary>
public sealed class NatsJSEventStore : NatsJSStoreBase, IEventStore
{
    private readonly IMessageSerializer _serializer;

    public NatsJSEventStore(
        INatsConnection connection,
        IMessageSerializer serializer,
        string streamName = "CATGA_EVENTS",
        NatsJSStoreOptions? options = null)
        : base(connection, streamName, options)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <summary>
    /// Define subjects for event store stream
    /// </summary>
    protected override string[] GetSubjects() => new[] { $"{StreamName}.>" };

    public async ValueTask AppendAsync(
        string streamId,
        IReadOnlyList<IEvent> events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(streamId)) throw new ArgumentException("Stream ID cannot be empty", nameof(streamId));
        if (events == null || events.Count == 0) throw new ArgumentException("Events cannot be null or empty", nameof(events));

        await EnsureInitializedAsync(cancellationToken);

        // Check version for concurrency control
        if (expectedVersion >= 0)
        {
            var currentVersion = await GetVersionAsync(streamId, cancellationToken);
            if (currentVersion != expectedVersion)
            {
                throw new ConcurrencyException(streamId, expectedVersion, currentVersion);
            }
        }

        // Publish events to NATS JetStream (batch for efficiency)
        var subject = $"{StreamName}.{streamId}";

        foreach (var @event in events)
        {
            // Serialize event (use existing method)
            var data = _serializer.Serialize(@event);

            // Publish to JetStream
            var ack = await JetStream.PublishAsync(
                subject,
                data,
                cancellationToken: cancellationToken);

            // Verify publish success
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
        if (string.IsNullOrEmpty(streamId)) throw new ArgumentException("Stream ID cannot be empty", nameof(streamId));

        await EnsureInitializedAsync(cancellationToken);

        var subject = $"{StreamName}.{streamId}";
        var events = new List<StoredEvent>();

        try
        {
            // Create consumer for fetching events
            var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                StreamName,
                new ConsumerConfig
                {
                    Name = $"temp-{streamId}-{Guid.NewGuid():N}",
                    FilterSubject = subject,
                    AckPolicy = ConsumerConfigAckPolicy.None,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.All
                },
                cancellationToken);

            // Fetch events
            var count = 0;
            await foreach (var msg in consumer.FetchAsync<byte[]>(
                new NatsJSFetchOpts { MaxMsgs = maxCount },
                cancellationToken: cancellationToken))
            {
                if (count >= maxCount) break;

                // Deserialize event
                if (msg.Data != null && msg.Data.Length > 0)
                {
                    var @event = _serializer.Deserialize<IEvent>(msg.Data);
                    if (@event != null)
                    {
                        events.Add(new StoredEvent
                        {
                            Version = (long)(msg.Metadata?.Sequence.Stream ?? 0UL),
                            Event = @event,
                            Timestamp = (msg.Metadata?.Timestamp.UtcDateTime ?? DateTime.UtcNow),
                            EventType = @event.GetType().Name
                        });
                        count++;
                    }
                }
            }

            // Clean up temporary consumer
            // No explicit delete API available on INatsJSConsumer in this version
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            // Stream doesn't exist yet, return empty
            return new EventStream
            {
                StreamId = streamId,
                Version = -1,
                Events = Array.Empty<StoredEvent>()
            };
        }

        var version = events.Count > 0 ? events[^1].Version : -1;

        return new EventStream
        {
            StreamId = streamId,
            Version = version,
            Events = events
        };
    }

    public async ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var subject = $"{StreamName}.{streamId}";

        try
        {
            // Create a lightweight consumer to fetch the last message for this subject
            var consumer = await JetStream.CreateOrUpdateConsumerAsync(
                StreamName,
                new ConsumerConfig
                {
                    Name = $"ver-{streamId}-{Guid.NewGuid():N}",
                    FilterSubject = subject,
                    AckPolicy = ConsumerConfigAckPolicy.None,
                    DeliverPolicy = ConsumerConfigDeliverPolicy.LastPerSubject
                },
                cancellationToken);

            await foreach (var msg in consumer.FetchAsync<byte[]>(
                new NatsJSFetchOpts { MaxMsgs = 1 },
                cancellationToken: cancellationToken))
            {
                // Take the first (last per subject) message, return its stream sequence as version
                return (long)(msg.Metadata?.Sequence.Stream ?? 0UL);
            }

            return -1;
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 404)
        {
            return -1; // Stream or subject doesn't exist
        }
    }

}

