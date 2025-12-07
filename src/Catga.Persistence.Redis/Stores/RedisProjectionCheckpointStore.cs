using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>
/// Redis-based projection checkpoint store.
/// </summary>
public sealed class RedisProjectionCheckpointStore : IProjectionCheckpointStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;
    private readonly string _prefix;

    public RedisProjectionCheckpointStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        string prefix = "projection:checkpoint:")
    {
        _redis = redis;
        _serializer = serializer;
        _provider = provider;
        _prefix = prefix;
    }

    public async ValueTask SaveAsync(ProjectionCheckpoint checkpoint, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + checkpoint.ProjectionName;

            await db.HashSetAsync(key, [
                new HashEntry("position", checkpoint.Position),
                new HashEntry("lastUpdated", checkpoint.LastUpdated.Ticks),
                new HashEntry("streamId", checkpoint.StreamId ?? "")
            ]);
        }, ct);
    }

    public async ValueTask<ProjectionCheckpoint?> LoadAsync(string projectionName, CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + projectionName;

            var entries = await db.HashGetAllAsync(key);
            if (entries.Length == 0) return null;

            var position = (long)entries.FirstOrDefault(e => e.Name == "position").Value;
            var lastUpdatedTicks = (long)entries.FirstOrDefault(e => e.Name == "lastUpdated").Value;
            var streamId = (string?)entries.FirstOrDefault(e => e.Name == "streamId").Value;

            return new ProjectionCheckpoint
            {
                ProjectionName = projectionName,
                Position = position,
                LastUpdated = new DateTime(lastUpdatedTicks, DateTimeKind.Utc),
                StreamId = string.IsNullOrEmpty(streamId) ? null : streamId
            };
        }, ct);
    }

    public async ValueTask DeleteAsync(string projectionName, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + projectionName;
            await db.KeyDeleteAsync(key);
        }, ct);
    }
}
