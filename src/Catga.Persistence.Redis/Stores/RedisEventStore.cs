using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.EventSourcing;
using Catga.Hosting;
using Catga.Observability;
using Catga.Resilience;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>
/// Redis Streams-based event store with optimistic concurrency.
/// Uses Redis Streams for event storage with version tracking.
/// </summary>
public sealed partial class RedisEventStore : IEventStore, IHealthCheckable, Hosting.IRecoverableComponent
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;
    private readonly IEventTypeRegistry _registry;
    private readonly ILogger<RedisEventStore> _logger;
    private readonly string _prefix;
    private bool _isHealthy = true;
    private string? _healthStatus = "Initialized";
    private DateTimeOffset? _lastHealthCheck;
    
    /// <inheritdoc/>
    public bool IsHealthy => _isHealthy;
    
    /// <inheritdoc/>
    public string? HealthStatus => _healthStatus;
    
    /// <inheritdoc/>
    public DateTimeOffset? LastHealthCheck => _lastHealthCheck;
    
    /// <inheritdoc/>
    public string ComponentName => "RedisEventStore";

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
        // No retry for append - has optimistic concurrency control
        await _provider.ExecutePersistenceNoRetryAsync(async ct =>
        {
            var start = MetricsHelper.StartTimestamp();
            using var activity = MetricsHelper.StartPersistenceActivity("EventStore", "Append");

            ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
            ArgumentNullException.ThrowIfNull(events);

            try
            {
                if (events.Count == 0) return;

                var db = _redis.GetDatabase();
                var streamKey = _prefix + streamId;
                var versionKey = _prefix + "version:" + streamId;

                // Check expected version with optimistic concurrency
                var currentVersion = await GetVersionInternalAsync(db, versionKey);
                
                // expectedVersion = -1 means "append to current version" (no concurrency check)
                // For other values, enforce strict version matching
                if (expectedVersion != -1 && currentVersion != expectedVersion)
                {
                    var ex = new ConcurrencyException(streamId, expectedVersion, currentVersion);
                    MetricsHelper.SetActivityError(activity, ex);
                    throw ex;
                }

                // Append events to stream using Lua script for atomicity
                // Use a single Lua script to append all events atomically
                var script = @"
                local versionKey = KEYS[1]
                local streamKey = KEYS[2]
                local expectedVer = tonumber(ARGV[1])
                local eventCount = tonumber(ARGV[2])
                
                local currentVer = redis.call('GET', versionKey)
                if currentVer == false then
                    currentVer = -1
                else
                    currentVer = tonumber(currentVer)
                end
                
                if expectedVer ~= -1 and currentVer ~= expectedVer then
                    return {err = 'Version mismatch: expected ' .. expectedVer .. ', got ' .. currentVer}
                end
                
                local newVer = currentVer
                for i = 1, eventCount do
                    newVer = newVer + 1
                    local offset = 2 + (i - 1) * 3
                    local type = ARGV[offset + 1]
                    local data = ARGV[offset + 2]
                    local timestamp = ARGV[offset + 3]
                    redis.call('XADD', streamKey, '*', 'version', newVer, 'type', type, 'data', data, 'timestamp', timestamp)
                end
                
                redis.call('SET', versionKey, newVer)
                return newVer
            ";

                // Prepare arguments: expectedVersion, eventCount, then for each event: type, data, timestamp
                var args = new List<RedisValue> { expectedVersion, events.Count };
                foreach (var @event in events)
                {
                    var runtimeType = _registry.GetPreservedType(@event);
                    var typeFull = runtimeType.AssemblyQualifiedName ?? runtimeType.FullName!;
                    var data = _serializer.Serialize(@event, runtimeType);
                    var timestamp = DateTime.UtcNow.Ticks;
                    
                    args.Add(typeFull);
                    args.Add(data);
                    args.Add(timestamp);
                }

                var result = await db.ScriptEvaluateAsync(script,
                    keys: new RedisKey[] { versionKey, streamKey },
                    values: args.ToArray());

                if (result.IsNull)
                {
                    var actualVersion = await GetVersionInternalAsync(db, versionKey);
                    var ex = new ConcurrencyException(streamId, expectedVersion, actualVersion);
                    MetricsHelper.SetActivityError(activity, ex);
                    throw ex;
                }

                var finalVersion = (long)result;

                activity?.AddEvent(new ActivityEvent("events.appended",
                    tags: new ActivityTagsCollection
                    {
                        { "stream", streamId },
                        { "version", finalVersion },
                        { "event.count", events.Count }
                    }));

                MetricsHelper.RecordEventStoreAppend(events.Count, start);
                LogEventsAppended(_logger, streamId, events.Count, finalVersion);
                
                UpdateHealthStatus(true);
            }
            catch (Exception ex)
            {
                UpdateHealthStatus(false, ex.Message);
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
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            var start = MetricsHelper.StartTimestamp();
            using var activity = MetricsHelper.StartPersistenceActivity("EventStore", "Read");

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

                    // Add all events first, then filter by version
                    storedEvents.Add(new StoredEvent
                    {
                        Version = version,
                        Event = @event,
                        Timestamp = new DateTime(timestamp, DateTimeKind.Utc),
                        EventType = eventType.Name
                    });
                }
                
                // Filter by fromVersion after collecting all events
                storedEvents = storedEvents.Where(e => e.Version >= fromVersion).Take(maxCount).ToList();
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

            MetricsHelper.RecordEventStoreRead(start);

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
            var start = MetricsHelper.StartTimestamp();
            using var activity = MetricsHelper.StartPersistenceActivity("EventStore", "ReadToVersion");

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

            MetricsHelper.RecordEventStoreRead(start);

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
            var start = MetricsHelper.StartTimestamp();
            using var activity = MetricsHelper.StartPersistenceActivity("EventStore", "ReadToTimestamp");

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

            MetricsHelper.RecordEventStoreRead(start);

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

    #region Projection API

    public async ValueTask<IReadOnlyList<string>> GetAllStreamIdsAsync(CancellationToken cancellationToken = default)
    {
        return await _provider.ExecutePersistenceAsync(async ct =>
        {
            var db = _redis.GetDatabase();
            var server = _redis.GetServers().First();
            var streamIds = new List<string>();

            // Scan for all version keys to find stream IDs
            var pattern = _prefix + "version:*";
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                var keyStr = key.ToString();
                var streamId = keyStr[(_prefix + "version:").Length..];
                streamIds.Add(streamId);
            }

            return (IReadOnlyList<string>)streamIds;
        }, cancellationToken);
    }

    #endregion

    /// <inheritdoc/>
    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify connection by attempting a simple operation
            var db = _redis.GetDatabase();
            await db.PingAsync();
            
            _isHealthy = true;
            _healthStatus = "Recovered successfully";
            _lastHealthCheck = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _isHealthy = false;
            _healthStatus = $"Recovery failed: {ex.Message}";
            _lastHealthCheck = DateTimeOffset.UtcNow;
            throw;
        }
    }
    
    /// <summary>
    /// Updates health status based on operation results
    /// </summary>
    private void UpdateHealthStatus(bool success, string? errorMessage = null)
    {
        _isHealthy = success;
        _healthStatus = success ? "Healthy" : $"Unhealthy: {errorMessage}";
        _lastHealthCheck = DateTimeOffset.UtcNow;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Events appended to stream {StreamId}: count={Count}, version={Version}")]
    private static partial void LogEventsAppended(ILogger logger, string streamId, int count, long version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unknown event type {TypeName} in stream {StreamId}")]
    private static partial void LogUnknownEventType(ILogger logger, string typeName, string streamId);
}
