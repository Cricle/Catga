using Catga.EventSourcing;
using Catga.Resilience;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>Redis-based projection checkpoint store.</summary>
public sealed class RedisProjectionCheckpointStore(IConnectionMultiplexer redis, IResiliencePipelineProvider provider, string prefix = "projection:checkpoint:") : IProjectionCheckpointStore
{
    public async ValueTask SaveAsync(ProjectionCheckpoint checkpoint, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            await redis.GetDatabase().HashSetAsync(prefix + checkpoint.ProjectionName, [
                new("position", checkpoint.Position), new("lastUpdated", checkpoint.LastUpdated.Ticks), new("streamId", checkpoint.StreamId ?? "")
            ]);
        }, ct);

    public async ValueTask<ProjectionCheckpoint?> LoadAsync(string projectionName, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            var entries = await redis.GetDatabase().HashGetAllAsync(prefix + projectionName);
            if (entries.Length == 0) return null;
            var d = entries.ToDictionary(e => (string)e.Name!, e => e.Value);
            return new ProjectionCheckpoint
            {
                ProjectionName = projectionName, Position = (long)d["position"],
                LastUpdated = new((long)d["lastUpdated"], DateTimeKind.Utc),
                StreamId = string.IsNullOrEmpty((string?)d["streamId"]) ? null : (string?)d["streamId"]
            };
        }, ct);

    public async ValueTask DeleteAsync(string projectionName, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ => await redis.GetDatabase().KeyDeleteAsync(prefix + projectionName), ct);
}
