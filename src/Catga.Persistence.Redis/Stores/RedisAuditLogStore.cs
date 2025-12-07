using Catga.Abstractions;
using Catga.EventSourcing;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Stores;

/// <summary>
/// Redis-based audit log store.
/// </summary>
public sealed class RedisAuditLogStore : IAuditLogStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly string _prefix;

    public RedisAuditLogStore(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        string prefix = "audit:")
    {
        _redis = redis;
        _serializer = serializer;
        _prefix = prefix;
    }

    public async ValueTask LogAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{_prefix}stream:{entry.StreamId}";
        var globalKey = $"{_prefix}global";
        var data = _serializer.Serialize(entry, typeof(AuditLogEntry));

        var score = entry.Timestamp.Ticks;
        await db.SortedSetAddAsync(key, data, score);
        await db.SortedSetAddAsync(globalKey, data, score);
    }

    public async ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsAsync(
        string streamId,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{_prefix}stream:{streamId}";
        var entries = await db.SortedSetRangeByRankAsync(key, 0, -1);

        var result = new List<AuditLogEntry>();
        foreach (var entry in entries)
        {
            if (entry.HasValue)
            {
                var log = _serializer.Deserialize<AuditLogEntry>((byte[])entry!);
                if (log != null) result.Add(log);
            }
        }

        return result;
    }

    public async ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsByTimeRangeAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{_prefix}global";
        var entries = await db.SortedSetRangeByScoreAsync(key, from.Ticks, to.Ticks);

        var result = new List<AuditLogEntry>();
        foreach (var entry in entries)
        {
            if (entry.HasValue)
            {
                var log = _serializer.Deserialize<AuditLogEntry>((byte[])entry!);
                if (log != null) result.Add(log);
            }
        }

        return result;
    }

    public async ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsByUserAsync(
        string userId,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{_prefix}global";
        var entries = await db.SortedSetRangeByRankAsync(key, 0, -1);

        var result = new List<AuditLogEntry>();
        foreach (var entry in entries)
        {
            if (entry.HasValue)
            {
                var log = _serializer.Deserialize<AuditLogEntry>((byte[])entry!);
                if (log != null && log.UserId == userId) result.Add(log);
            }
        }

        return result;
    }
}
