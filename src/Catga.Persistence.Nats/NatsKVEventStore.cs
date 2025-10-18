using Catga.EventSourcing;
using Catga.Messages;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
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
    private INatsKVStore? _kvStore;
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

            _kvStore = await _jetStream.CreateKeyValueStoreAsync(config, cancellationToken);
            _initialized = true;
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 400 && ex.Error.Description.Contains("already"))
        {
            // Bucket already exists, get it
            _kvStore = await _jetStream.GetKeyValueStoreAsync(_bucketName, cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task AppendAsync(
        string streamId,
        IEnumerable<IEvent> events,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(events);

        await EnsureInitializedAsync(cancellationToken);

        var eventList = events.ToList();
        if (eventList.Count == 0) return;

        // Get current version
        var currentVersion = await GetStreamVersionAsync(streamId, cancellationToken);

        // Optimistic concurrency check
        if (expectedVersion != -1 && currentVersion != expectedVersion)
        {
            throw new ConcurrencyException(
                $"Stream '{streamId}' version mismatch. Expected: {expectedVersion}, Actual: {currentVersion}");
        }

        // Append events
        var version = currentVersion;
        foreach (var @event in eventList)
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

    public async Task<List<IEvent>> ReadAsync(
        string streamId,
        long fromVersion = 0,
        long toVersion = long.MaxValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        await EnsureInitializedAsync(cancellationToken);

        var events = new List<IEvent>();
        var version = await GetStreamVersionAsync(streamId, cancellationToken);

        if (version == 0) return events;

        var start = Math.Max(1, fromVersion);
        var end = Math.Min(version, toVersion);

        for (var i = start; i <= end; i++)
        {
            var key = GetEventKey(streamId, i);
            try
            {
                var entry = await _kvStore!.GetEntryAsync<byte[]>(key, cancellationToken: cancellationToken);
                if (entry?.Value != null)
                {
                    var @event = DeserializeEvent(entry.Value);
                    events.Add(@event);
                }
            }
            catch (NatsKVKeyNotFoundException)
            {
                // Event not found, skip
            }
        }

        return events;
    }

    public async Task<bool> ExistsAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        await EnsureInitializedAsync(cancellationToken);

        try
        {
            var metadata = await _kvStore!.GetEntryAsync<byte[]>(
                GetMetadataKey(streamId),
                cancellationToken: cancellationToken);
            return metadata != null;
        }
        catch (NatsKVKeyNotFoundException)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        await EnsureInitializedAsync(cancellationToken);

        var version = await GetStreamVersionAsync(streamId, cancellationToken);

        // Delete all events
        for (var i = 1L; i <= version; i++)
        {
            try
            {
                await _kvStore!.DeleteAsync(GetEventKey(streamId, i), cancellationToken: cancellationToken);
            }
            catch (NatsKVKeyNotFoundException)
            {
                // Already deleted
            }
        }

        // Delete metadata
        try
        {
            await _kvStore!.DeleteAsync(GetMetadataKey(streamId), cancellationToken: cancellationToken);
        }
        catch (NatsKVKeyNotFoundException)
        {
            // Already deleted
        }
    }

    private async Task<long> GetStreamVersionAsync(string streamId, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await _kvStore!.GetEntryAsync<byte[]>(
                GetMetadataKey(streamId),
                cancellationToken: cancellationToken);

            if (entry?.Value != null)
            {
                var metadata = JsonSerializer.Deserialize<StreamMetadata>(entry.Value);
                return metadata?.Version ?? 0;
            }
        }
        catch (NatsKVKeyNotFoundException)
        {
            // Stream doesn't exist
        }

        return 0;
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

