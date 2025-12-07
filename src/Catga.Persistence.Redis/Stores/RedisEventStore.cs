using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.EventSourcing;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>
/// Redis Streams-based event store with optimistic concurrency.
/// Uses Redis Streams for event storage with version tracking.
/// </summary>
public sealed partial class RedisEventStore : IEventStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;
    private readonly IEventTypeRegistry _registry;
    private readonly ILogger<RedisEventStore> _logger;
    private readonly string _prefix;

    public RedisEventStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        ILogger<RedisEventStore> logger,
        IEventTypeRegistry? registry = null,
        string prefix = "events:")
    {
        _redis = redis;
        _serializer = serializer;
        _provider = provider;
        _logger = logger;
        _registry = registry ?? new DefaultEventTypeRegistry();
        _prefix = prefix;
    }

    public async ValueTask AppendAsync(
        string streamId,
        IReadOnlyList<IEvent> events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        await _provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.EventStore.Append", ActivityKind.Producer);

            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            ArgumentNullException.ThrowIfNull(events);

            if (events.Count == 0) return;

            var db = _redis.GetDatabase();
            var streamKey = _prefix + streamId;
            var versionKey = _prefix + "version:" + streamId;

            // Check expected version with optimistic concurrency
            var currentVersion = await GetVersionInternalAsync(db, versionKey);
            if (expectedVersion != -1 && currentVersion != expectedVersion)
            {
                var ex = new ConcurrencyException(streamId, expectedVersion, currentVersion);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw ex;
            }

            // Append events to stream
            var newVersion = currentVersion;
            foreach (var @event in events)
            {
                newVersion++;
                var runtimeType = @event.GetType();
                var typeFull = runtimeType.AssemblyQualifiedName ?? runtimeType.FullName!;
                _registry.Register(typeFull, runtimeType);

                var data = _serializer.Serialize(@event, runtimeType);
                var timestamp = DateTime.UtcNow.Ticks;

                await db.StreamAddAsync(streamKey, [
                    new NameValueEntry("version", newVersion),
                    new NameValueEntry("type", typeFull),
                    new NameValueEntry("data", data),
                    new NameValueEntry("timestamp", timestamp)
                ]);

                activity?.AddEvent(new ActivityEvent("event.appended",
                    tags: new ActivityTagsCollection
                    {
                        { "stream", streamId },
                        { "version", newVersion },
                        { "event.type", runtimeType.Name }
                    }));
            }

            // Update version
            await db.StringSetAsync(versionKey, newVersion);

            CatgaDiagnostics.EventStoreAppends.Add(events.Count);
            CatgaDiagnostics.EventStoreAppendDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            LogEventsAppended(_logger, streamId, events.Count, newVersion);
        }, cancellationToken);
    }

    public async ValueTask<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.EventStore.Read", ActivityKind.Internal);

            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

            var db = _redis.GetDatabase();
            var streamKey = _prefix + streamId;

            var storedEvents = new List<StoredEvent>();

            try
            {
                var entries = await db.StreamReadAsync(streamKey, "0-0", maxCount);
                if (entries.Length == 0)
                {
                    return new EventStream
                    {
                        StreamId = streamId,
                        Version = -1,
                        Events = []
                    };
                }

                foreach (var entry in entries)
                {
                    var version = (long)entry["version"];
                    if (version < fromVersion) continue;

                    var typeName = (string)entry["type"]!;
                    var data = (byte[])entry["data"]!;
                    var timestamp = (long)entry["timestamp"];

                    var eventType = _registry.Resolve(typeName);
                    if (eventType == null)
                    {
                        LogUnknownEventType(_logger, typeName, streamId);
                        continue;
                    }

                    var @event = (IEvent?)_serializer.Deserialize(data, eventType);
                    if (@event == null) continue;

                    storedEvents.Add(new StoredEvent
                    {
                        Version = version,
                        Event = @event,
                        Timestamp = new DateTime(timestamp, DateTimeKind.Utc),
                        EventType = eventType.Name
                    });

                    if (storedEvents.Count >= maxCount) break;
                }
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP") || ex.Message.Contains("no such key"))
            {
                return new EventStream
                {
                    StreamId = streamId,
                    Version = -1,
                    Events = []
                };
            }

            var finalVersion = storedEvents.Count > 0 ? storedEvents[^1].Version : -1;

            CatgaDiagnostics.EventStoreReads.Add(1);
            CatgaDiagnostics.EventStoreReadDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

            return new EventStream
            {
                StreamId = streamId,
                Version = finalVersion,
                Events = storedEvents
            };
        }, cancellationToken);
    }

    public async ValueTask<long> GetVersionAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

            var db = _redis.GetDatabase();
            var versionKey = _prefix + "version:" + streamId;

            return await GetVersionInternalAsync(db, versionKey);
        }, cancellationToken);
    }

    private static async Task<long> GetVersionInternalAsync(IDatabase db, string versionKey)
    {
        var value = await db.StringGetAsync(versionKey);
        return value.HasValue ? (long)value : -1;
    }

    #region Time Travel API

    public async ValueTask<EventStream> ReadToVersionAsync(
        string streamId,
        long toVersion,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.EventStore.ReadToVersion", ActivityKind.Internal);

            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

            var db = _redis.GetDatabase();
            var streamKey = _prefix + streamId;
            var storedEvents = new List<StoredEvent>();

            try
            {
                var entries = await db.StreamReadAsync(streamKey, "0-0");
                if (entries.Length == 0)
                {
                    return new EventStream { StreamId = streamId, Version = -1, Events = [] };
                }

                foreach (var entry in entries)
                {
                    var version = (long)entry["version"];
                    if (version > toVersion) break;

                    var typeName = (string)entry["type"]!;
                    var data = (byte[])entry["data"]!;
                    var timestamp = (long)entry["timestamp"];

                    var eventType = _registry.Resolve(typeName);
                    if (eventType == null)
                    {
                        LogUnknownEventType(_logger, typeName, streamId);
                        continue;
                    }

                    var @event = (IEvent?)_serializer.Deserialize(data, eventType);
                    if (@event == null) continue;

                    storedEvents.Add(new StoredEvent
                    {
                        Version = version,
                        Event = @event,
                        Timestamp = new DateTime(timestamp, DateTimeKind.Utc),
                        EventType = eventType.Name
                    });
                }
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP") || ex.Message.Contains("no such key"))
            {
                return new EventStream { StreamId = streamId, Version = -1, Events = [] };
            }

            var finalVersion = storedEvents.Count > 0 ? storedEvents[^1].Version : -1;

            CatgaDiagnostics.EventStoreReads.Add(1);
            CatgaDiagnostics.EventStoreReadDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

            return new EventStream { StreamId = streamId, Version = finalVersion, Events = storedEvents };
        }, cancellationToken);
    }

    public async ValueTask<EventStream> ReadToTimestampAsync(
        string streamId,
        DateTime upperBound,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            var start = Stopwatch.GetTimestamp();
            using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Persistence.EventStore.ReadToTimestamp", ActivityKind.Internal);

            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

            var db = _redis.GetDatabase();
            var streamKey = _prefix + streamId;
            var storedEvents = new List<StoredEvent>();
            var upperBoundTicks = upperBound.Ticks;

            try
            {
                var entries = await db.StreamReadAsync(streamKey, "0-0");
                if (entries.Length == 0)
                {
                    return new EventStream { StreamId = streamId, Version = -1, Events = [] };
                }

                foreach (var entry in entries)
                {
                    var timestamp = (long)entry["timestamp"];
                    if (timestamp > upperBoundTicks) break;

                    var version = (long)entry["version"];
                    var typeName = (string)entry["type"]!;
                    var data = (byte[])entry["data"]!;

                    var eventType = _registry.Resolve(typeName);
                    if (eventType == null)
                    {
                        LogUnknownEventType(_logger, typeName, streamId);
                        continue;
                    }

                    var @event = (IEvent?)_serializer.Deserialize(data, eventType);
                    if (@event == null) continue;

                    storedEvents.Add(new StoredEvent
                    {
                        Version = version,
                        Event = @event,
                        Timestamp = new DateTime(timestamp, DateTimeKind.Utc),
                        EventType = eventType.Name
                    });
                }
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP") || ex.Message.Contains("no such key"))
            {
                return new EventStream { StreamId = streamId, Version = -1, Events = [] };
            }

            var finalVersion = storedEvents.Count > 0 ? storedEvents[^1].Version : -1;

            CatgaDiagnostics.EventStoreReads.Add(1);
            CatgaDiagnostics.EventStoreReadDuration.Record((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);

            return new EventStream { StreamId = streamId, Version = finalVersion, Events = storedEvents };
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<VersionInfo>> GetVersionHistoryAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

            var db = _redis.GetDatabase();
            var streamKey = _prefix + streamId;
            var history = new List<VersionInfo>();

            try
            {
                var entries = await db.StreamReadAsync(streamKey, "0-0");
                foreach (var entry in entries)
                {
                    var version = (long)entry["version"];
                    var typeName = (string)entry["type"]!;
                    var timestamp = (long)entry["timestamp"];

                    history.Add(new VersionInfo
                    {
                        Version = version,
                        Timestamp = new DateTime(timestamp, DateTimeKind.Utc),
                        EventType = typeName.Contains('.') ? typeName[(typeName.LastIndexOf('.') + 1)..] : typeName
                    });
                }
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP") || ex.Message.Contains("no such key"))
            {
                return (IReadOnlyList<VersionInfo>)Array.Empty<VersionInfo>();
            }

            return (IReadOnlyList<VersionInfo>)history;
        }, cancellationToken);
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Events appended to stream {StreamId}: count={Count}, version={Version}")]
    private static partial void LogEventsAppended(ILogger logger, string streamId, int count, long version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown event type {TypeName} in stream {StreamId}")]
    private static partial void LogUnknownEventType(ILogger logger, string typeName, string streamId);
}
