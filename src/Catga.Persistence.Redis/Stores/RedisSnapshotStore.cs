using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.EventSourcing;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>
/// Redis-based snapshot store using MemoryPack serialization.
/// AOT-compatible, low-allocation implementation.
/// </summary>
public sealed partial class RedisSnapshotStore : ISnapshotStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly SnapshotOptions _options;
    private readonly ILogger<RedisSnapshotStore> _logger;

    public RedisSnapshotStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        IOptions<SnapshotOptions> options,
        ILogger<RedisSnapshotStore> logger)
    {
        _redis = redis;
        _serializer = serializer;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        TAggregate aggregate,
        long version,
        CancellationToken ct = default) where TAggregate : class
    {
        var db = _redis.GetDatabase();
        var key = GetKey(streamId);

        var stateBytes = _serializer.Serialize(aggregate);
        var metadata = new SnapshotMetadata
        {
            StreamId = streamId,
            Version = version,
            Timestamp = DateTime.UtcNow,
            AggregateType = typeof(TAggregate).AssemblyQualifiedName ?? typeof(TAggregate).FullName ?? typeof(TAggregate).Name,
            StateLength = stateBytes.Length
        };

        var metadataBytes = MemoryPackSerializer.Serialize(metadata);

        // Store as hash: metadata + state
        var entries = new HashEntry[]
        {
            new("metadata", metadataBytes),
            new("state", stateBytes)
        };

        await db.HashSetAsync(key, entries);
        LogSnapshotSaved(_logger, streamId, version);
    }

    public async ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        CancellationToken ct = default) where TAggregate : class
    {
        var db = _redis.GetDatabase();
        var key = GetKey(streamId);

        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0)
            return null;

        byte[]? metadataBytes = null;
        byte[]? stateBytes = null;

        foreach (var entry in entries)
        {
            if (entry.Name == "metadata")
                metadataBytes = entry.Value;
            else if (entry.Name == "state")
                stateBytes = entry.Value;
        }

        if (metadataBytes == null || stateBytes == null)
            return null;

        var metadata = MemoryPackSerializer.Deserialize<SnapshotMetadata>(metadataBytes);
        if (metadata == null)
            return null;

        var state = _serializer.Deserialize<TAggregate>(stateBytes);
        if (state == null)
            return null;

        LogSnapshotLoaded(_logger, streamId, metadata.Version);

        return new Snapshot<TAggregate>
        {
            StreamId = streamId,
            State = state,
            Version = metadata.Version,
            Timestamp = metadata.Timestamp
        };
    }

    public async ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = GetKey(streamId);
        await db.KeyDeleteAsync(key);
        LogSnapshotDeleted(_logger, streamId);
    }

    private string GetKey(string streamId) => string.Concat(_options.KeyPrefix, streamId);

    #region Logging

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot saved: {StreamId} at version {Version}")]
    private static partial void LogSnapshotSaved(ILogger logger, string streamId, long version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot loaded: {StreamId} at version {Version}")]
    private static partial void LogSnapshotLoaded(ILogger logger, string streamId, long version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot deleted: {StreamId}")]
    private static partial void LogSnapshotDeleted(ILogger logger, string streamId);

    #endregion
}

/// <summary>Internal snapshot metadata.</summary>
[MemoryPackable]
internal partial class SnapshotMetadata
{
    public string StreamId { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTime Timestamp { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public int StateLength { get; set; }
}
