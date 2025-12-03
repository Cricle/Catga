using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.EventSourcing;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>Redis-based snapshot store.</summary>
public sealed partial class RedisSnapshotStore(IConnectionMultiplexer redis, IMessageSerializer serializer, IOptions<SnapshotOptions> options, ILogger<RedisSnapshotStore> logger) : ISnapshotStore
{
    private readonly SnapshotOptions _opts = options.Value;

    public async ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(string streamId, TAggregate aggregate, long version, CancellationToken ct = default) where TAggregate : class
    {
        var state = serializer.Serialize(aggregate);
        var meta = MemoryPackSerializer.Serialize(new Meta { StreamId = streamId, Version = version, Timestamp = DateTime.UtcNow, AggregateType = typeof(TAggregate).AssemblyQualifiedName ?? typeof(TAggregate).Name, StateLength = state.Length });
        await redis.GetDatabase().HashSetAsync(_opts.KeyPrefix + streamId, [new("metadata", meta), new("state", state)]);
        LogSnapshotSaved(logger, streamId, version);
    }

    public async ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(string streamId, CancellationToken ct = default) where TAggregate : class
    {
        var entries = await redis.GetDatabase().HashGetAllAsync(_opts.KeyPrefix + streamId);
        if (entries.Length == 0) return null;
        byte[]? metaBytes = null, stateBytes = null;
        foreach (var e in entries) { if (e.Name == "metadata") metaBytes = e.Value; else if (e.Name == "state") stateBytes = e.Value; }
        if (metaBytes == null || stateBytes == null) return null;
        var meta = MemoryPackSerializer.Deserialize<Meta>(metaBytes);
        var state = serializer.Deserialize<TAggregate>(stateBytes);
        if (meta == null || state == null) return null;
        LogSnapshotLoaded(logger, streamId, meta.Version);
        return new() { StreamId = streamId, State = state, Version = meta.Version, Timestamp = meta.Timestamp };
    }

    public async ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
    {
        await redis.GetDatabase().KeyDeleteAsync(_opts.KeyPrefix + streamId);
        LogSnapshotDeleted(logger, streamId);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot saved: {StreamId} at version {Version}")]
    private static partial void LogSnapshotSaved(ILogger logger, string streamId, long version);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot loaded: {StreamId} at version {Version}")]
    private static partial void LogSnapshotLoaded(ILogger logger, string streamId, long version);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Snapshot deleted: {StreamId}")]
    private static partial void LogSnapshotDeleted(ILogger logger, string streamId);
}

[MemoryPackable]
internal partial class Meta { public string StreamId { get; set; } = ""; public long Version { get; set; } public DateTime Timestamp { get; set; } public string AggregateType { get; set; } = ""; public int StateLength { get; set; } }
