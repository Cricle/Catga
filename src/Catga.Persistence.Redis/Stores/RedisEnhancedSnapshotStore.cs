using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Resilience;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>
/// Redis-based enhanced snapshot store with multi-version support.
/// </summary>
public sealed class RedisEnhancedSnapshotStore : IEnhancedSnapshotStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly IResiliencePipelineProvider _provider;
    private readonly string _prefix;

    public RedisEnhancedSnapshotStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        IResiliencePipelineProvider provider,
        string prefix = "snapshot:")
    {
        _redis = redis;
        _serializer = serializer;
        _provider = provider;
        _prefix = prefix;
    }

    public async ValueTask SaveAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        TAggregate aggregate,
        long version,
        CancellationToken ct = default) where TAggregate : class
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + streamId;
            var data = _serializer.Serialize(aggregate, typeof(TAggregate));
            var timestamp = DateTime.UtcNow.Ticks;

            // Store as sorted set with version as score
            await db.SortedSetAddAsync(key, [
                new SortedSetEntry(
                    $"{version}:{timestamp}:{Convert.ToBase64String(data)}",
                    version)
            ]);
        }, ct);
    }

    public async ValueTask<Snapshot<TAggregate>?> LoadAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        CancellationToken ct = default) where TAggregate : class
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + streamId;

            // Get highest version snapshot
            var entries = await db.SortedSetRangeByRankAsync(key, -1, -1);
            if (entries.Length == 0) return null;

            return ParseSnapshot<TAggregate>(streamId, entries[0]!);
        }, ct);
    }

    public async ValueTask<Snapshot<TAggregate>?> LoadAtVersionAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        long version,
        CancellationToken ct = default) where TAggregate : class
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + streamId;

            // Get highest version <= target version
            var entries = await db.SortedSetRangeByScoreAsync(key, double.NegativeInfinity, version, order: Order.Descending, take: 1);
            if (entries.Length == 0) return null;

            return ParseSnapshot<TAggregate>(streamId, entries[0]!);
        }, ct);
    }

    public async ValueTask<IReadOnlyList<SnapshotInfo>> GetSnapshotHistoryAsync(
        string streamId,
        CancellationToken ct = default)
    {
        return await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + streamId;

            var entries = await db.SortedSetRangeByRankWithScoresAsync(key);
            var history = new List<SnapshotInfo>();

            foreach (var entry in entries)
            {
                var parts = entry.Element.ToString().Split(':');
                if (parts.Length >= 2)
                {
                    var version = (long)entry.Score;
                    var timestamp = new DateTime(long.Parse(parts[1]), DateTimeKind.Utc);
                    history.Add(new SnapshotInfo(version, timestamp));
                }
            }

            return (IReadOnlyList<SnapshotInfo>)history;
        }, ct);
    }

    public async ValueTask DeleteAsync(string streamId, CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + streamId;
            await db.KeyDeleteAsync(key);
        }, ct);
    }

    public async ValueTask DeleteBeforeVersionAsync(
        string streamId,
        long version,
        CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + streamId;
            await db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, version - 1);
        }, ct);
    }

    public async ValueTask CleanupAsync(
        string streamId,
        int keepCount,
        CancellationToken ct = default)
    {
        await _provider.ExecutePersistenceAsync(async _ =>
        {
            var db = _redis.GetDatabase();
            var key = _prefix + streamId;

            var count = await db.SortedSetLengthAsync(key);
            if (count > keepCount)
            {
                // Remove oldest entries
                await db.SortedSetRemoveRangeByRankAsync(key, 0, (int)(count - keepCount - 1));
            }
        }, ct);
    }

    private Snapshot<TAggregate>? ParseSnapshot<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TAggregate>(
        string streamId,
        string entry) where TAggregate : class
    {
        var parts = entry.Split(':', 3);
        if (parts.Length < 3) return null;

        var version = long.Parse(parts[0]);
        var timestamp = new DateTime(long.Parse(parts[1]), DateTimeKind.Utc);
        var data = Convert.FromBase64String(parts[2]);
        var state = (TAggregate?)_serializer.Deserialize(data, typeof(TAggregate));

        if (state == null) return null;

        return new Snapshot<TAggregate>
        {
            StreamId = streamId,
            State = state,
            Version = version,
            Timestamp = timestamp
        };
    }
}
