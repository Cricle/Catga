using Catga.EventSourcing;
using Catga.Messages;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Catga.Persistence;

/// <summary>
/// NATS JetStream KV-based event store for persistent event sourcing
/// </summary>
public sealed class NatsKVEventStore : IEventStore, IAsyncDisposable
{
    private readonly INatsJSContext _jetStream;
    private readonly string _bucketName;
    private INatsKVContext? _kvStore;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public NatsKVEventStore(
        INatsJSContext jetStream,
        string? bucketName = null)
    {
        _jetStream = jetStream ?? throw new ArgumentNullException(nameof(jetStream));
        _bucketName = bucketName ?? "catga-events";
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            // Create or get KV bucket
            var config = new NatsKVConfig(_bucketName)
            {
                History = 64, // Keep version history
                MaxBucketSize = -1 // No size limit
            };

            _kvStore = await _jetStream.CreateKeyValueAsync(config, cancellationToken);
            _initialized = true;
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 400)
        {
            // Bucket already exists, get it
            _kvStore = await _jetStream.GetKeyValueAsync(_bucketName, cancellationToken);
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

        // Get current version
        var currentVersion = await GetVersionAsyncInternal(streamId, cancellationToken);

        // Optimistic concurrency check
        if (expectedVersion != -1 && currentVersion != expectedVersion)
        {
            throw new ConcurrencyException(streamId, expectedVersion, currentVersion);
        }

        // Append events
        var version = currentVersion;
        foreach (var @event in events)
        {
            version++;
            var key = GetEventKey(streamId, version);
            var data = SerializeEvent(@event);

            await _kvStore!.PutAsync(key, data, cancellationToken: cancellationToken);
        }

        // Update stream metadata
        var metadata = new StreamMetadata
        {
            StreamId = streamId,
            Version = version,
            EventCount = version,
            LastUpdated = DateTimeOffset.UtcNow
        };
        await _kvStore!.PutAsync(
            GetMetadataKey(streamId),
            JsonSerializer.SerializeToUtf8Bytes(metadata),
            cancellationToken: cancellationToken);
    }

    public async ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        await EnsureInitializedAsync(cancellationToken);

        var storedEvents = new List<StoredEvent>();
        var version = await GetVersionAsyncInternal(streamId, cancellationToken);

        if (version < 0)
        {
            return new EventStream
            {
                StreamId = streamId,
                Version = -1,
                Events = Array.Empty<StoredEvent>()
            };
        }

        var start = Math.Max(0, fromVersion);
        var end = Math.Min(version, start + maxCount - 1);

        for (var i = start; i <= end && storedEvents.Count < maxCount; i++)
        {
            var key = GetEventKey(streamId, i);
            try
            {
                var entry = await _kvStore!.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
                if (entry?.Value != null)
                {
                    var @event = DeserializeEvent(entry.Value);
                    storedEvents.Add(new StoredEvent
                    {
                        Version = i,
                        Event = @event,
                        Timestamp = entry.Created,
                        EventType = @event.GetType().Name
                    });
                }
            }
            catch (NatsKVKeyNotFoundException)
            {
                // Event not found, skip
            }
        }

        return new EventStream
        {
            StreamId = streamId,
            Version = version,
            Events = storedEvents
        };
    }

    public async ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        await EnsureInitializedAsync(cancellationToken);
        return await GetVersionAsyncInternal(streamId, cancellationToken);
    }

    private async Task<long> GetVersionAsyncInternal(string streamId, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await _kvStore!.GetEntryAsync<byte[]>(
                GetMetadataKey(streamId),
                cancellationToken: cancellationToken);

            if (entry?.Value != null)
            {
                var metadata = JsonSerializer.Deserialize<StreamMetadata>(entry.Value);
                return metadata?.Version ?? -1;
            }
        }
        catch (NatsKVKeyNotFoundException)
        {
            // Stream doesn't exist
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetEventKey(string streamId, long version)
        => $"event:{streamId}:{version:D19}";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetMetadataKey(string streamId)
        => $"meta:{streamId}";

    private static byte[] SerializeEvent(IEvent @event)
    {
        var envelope = new EventEnvelope
        {
            EventType = @event.GetType().AssemblyQualifiedName!,
            EventId = @event.EventId,
            AggregateId = @event.AggregateId,
            OccurredAt = @event.OccurredAt,
            Data = JsonSerializer.Serialize(@event, @event.GetType())
        };
        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }

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
        public Guid EventId { get; set; }
        public string AggregateId { get; set; } = string.Empty;
        public DateTimeOffset OccurredAt { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    private sealed class StreamMetadata
    {
        public string StreamId { get; set; } = string.Empty;
        public long Version { get; set; }
        public long EventCount { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
    }

    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
