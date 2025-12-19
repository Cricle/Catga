using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>Redis-based enhanced snapshot store with multi-version support.</summary>
public sealed class RedisEnhancedSnapshotStore(IConnectionMultiplexer redis, IMessageSerializer serializer, IResiliencePipelineProvider provider, string prefix = "snapshot:") : IEnhancedSnapshotStore
{
    public async ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId, TAggregate aggregate, long version, CancellationToken ct = default) where TAggregate : class
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            var data = serializer.Serialize(aggregate, typeof(TAggregate));
            await redis.GetDatabase().SortedSetAddAsync(prefix + streamId, 
                new SortedSetEntry[] { new($"{version}:{DateTime.UtcNow.Ticks}:{Convert.ToBase64String(data)}", version) });
        }, ct);

    public async ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId, CancellationToken ct = default) where TAggregate : class
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            var entries = await redis.GetDatabase().SortedSetRangeByRankAsync(prefix + streamId, -1, -1);
            return entries.Length == 0 ? null : ParseSnapshot<TAggregate>(streamId, entries[0]!);
        }, ct);

    public async ValueTask<Snapshot<TAggregate>?> LoadAtVersionAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId, long version, CancellationToken ct = default) where TAggregate : class
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            var entries = await redis.GetDatabase().SortedSetRangeByScoreAsync(prefix + streamId, double.NegativeInfinity, version, order: Order.Descending, take: 1);
            return entries.Length == 0 ? null : ParseSnapshot<TAggregate>(streamId, entries[0]!);
        }, ct);

    public async ValueTask<IReadOnlyList<SnapshotInfo>> GetSnapshotHistoryAsync(string streamId, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            var entries = await redis.GetDatabase().SortedSetRangeByRankWithScoresAsync(prefix + streamId);
            return (IReadOnlyList<SnapshotInfo>)entries.Select(e =>
            {
                var parts = e.Element.ToString().Split(':');
                return parts.Length >= 2 ? new SnapshotInfo((long)e.Score, new(long.Parse(parts[1]), DateTimeKind.Utc)) : null;
            }).Where(x => x != null).ToList()!;
        }, ct);

    public async ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ => await redis.GetDatabase().KeyDeleteAsync(prefix + streamId), ct);

    public async ValueTask DeleteBeforeVersionAsync(string streamId, long version, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ => await redis.GetDatabase().SortedSetRemoveRangeByScoreAsync(prefix + streamId, double.NegativeInfinity, version - 1), ct);

    public async ValueTask CleanupAsync(string streamId, int keepCount, CancellationToken ct = default)
        => await provider.ExecutePersistenceAsync(async _ =>
        {
            var db = redis.GetDatabase(); var count = await db.SortedSetLengthAsync(prefix + streamId);
            if (count > keepCount) await db.SortedSetRemoveRangeByRankAsync(prefix + streamId, 0, (int)(count - keepCount - 1));
        }, ct);

    private Snapshot<TAggregate>? ParseSnapshot<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(string streamId, string entry) where TAggregate : class
    {
        var parts = entry.Split(':', 3);
        if (parts.Length < 3) return null;
        var state = (TAggregate?)serializer.Deserialize(Convert.FromBase64String(parts[2]), typeof(TAggregate));
        return state == null ? null : new() { StreamId = streamId, State = state, Version = long.Parse(parts[0]), Timestamp = new(long.Parse(parts[1]), DateTimeKind.Utc) };
    }
}
