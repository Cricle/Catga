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
public sealed class NatsJSEventStore : IEventStore
{
    private readonly INatsConnection _connection;
    private readonly INatsJSContext _jetStream;
    private readonly IMessageSerializer _serializer;
    private readonly string _streamName;
    private volatile int _initializationState; // 0=未开始, 1=初始化中, 2=已完成
    private volatile bool _streamCreated;

    public NatsJSEventStore(
        INatsConnection connection,
        IMessageSerializer serializer,
        string streamName = "CATGA_EVENTS")
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _streamName = streamName ?? throw new ArgumentNullException(nameof(streamName));
        _jetStream = new NatsJSContext(_connection);
    }

    public async ValueTask AppendAsync(
        string streamId,
        IReadOnlyList<IEvent> events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(streamId)) throw new ArgumentException("Stream ID cannot be empty", nameof(streamId));
        if (events == null || events.Count == 0) throw new ArgumentException("Events cannot be null or empty", nameof(events));

        await EnsureStreamCreatedAsync(cancellationToken);

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
        var subject = $"{_streamName}.{streamId}";

        foreach (var @event in events)
        {
            // Serialize event (use existing method)
            var data = _serializer.Serialize(@event);

            // Publish to JetStream
            var ack = await _jetStream.PublishAsync(
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

        await EnsureStreamCreatedAsync(cancellationToken);

        var subject = $"{_streamName}.{streamId}";
        var events = new List<StoredEvent>();

        try
        {
            // Create consumer for fetching events
            var consumer = await _jetStream.CreateOrUpdateConsumerAsync(
                _streamName,
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
        await EnsureStreamCreatedAsync(cancellationToken);

        var subject = $"{_streamName}.{streamId}";

        try
        {
            // Create a lightweight consumer to fetch the last message for this subject
            var consumer = await _jetStream.CreateOrUpdateConsumerAsync(
                _streamName,
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

    /// <summary>
    /// Lock-free stream initialization using CAS pattern
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask EnsureStreamCreatedAsync(CancellationToken cancellationToken)
    {
        // Fast path: 已初始化则直接返回（零开销）
        if (_streamCreated) return;

        // CAS: 只有一个线程能成功从 0 -> 1
        if (Interlocked.CompareExchange(ref _initializationState, 1, 0) == 0)
        {
            try
            {
                try
                {
                    await _jetStream.CreateStreamAsync(
                        new StreamConfig(
                            _streamName,
                            new[] { $"{_streamName}.>" }
                        ),
                        cancellationToken);
                }
                catch (NatsJSApiException ex) when (ex.Error.Code == 400 && ex.Error.Description?.Contains("already exists") == true)
                {
                    // Stream already exists, ignore
                }

                _streamCreated = true;
                Interlocked.Exchange(ref _initializationState, 2);
            }
            catch
            {
                // 重置状态允许重试
                Interlocked.Exchange(ref _initializationState, 0);
                throw;
            }
        }
        else
        {
            // 等待初始化完成（自旋等待）
            var spinner = new SpinWait();
            while (Volatile.Read(ref _initializationState) == 1)
            {
                spinner.SpinOnce();
            }

            // 如果初始化失败，抛出异常
            if (!_streamCreated)
                throw new InvalidOperationException("Stream initialization failed by another thread.");
        }
    }
}

