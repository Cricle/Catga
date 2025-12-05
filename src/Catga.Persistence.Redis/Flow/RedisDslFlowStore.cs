using System.Diagnostics.CodeAnalysis;
using System.Text;
using Catga.Abstractions;
using Catga.Flow.Dsl;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Flow;

/// <summary>
/// Redis DSL flow store with atomic Lua scripts.
/// Supports distributed flow execution with WaitCondition for WhenAll/WhenAny.
/// </summary>
public sealed class RedisDslFlowStore : IDslFlowStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly string _prefix;

    // Lua scripts for atomic operations
    private const string CreateScript = @"
        if redis.call('EXISTS', KEYS[1]) == 1 then return 0 end
        redis.call('SET', KEYS[1], ARGV[1])
        redis.call('SADD', KEYS[2], ARGV[2])
        return 1";

    private const string UpdateScript = @"
        local current = redis.call('GET', KEYS[1])
        if not current then return 0 end
        local data = cjson.decode(current)
        if data.Version ~= tonumber(ARGV[1]) then return 0 end
        redis.call('SET', KEYS[1], ARGV[2])
        return 1";

    private const string DeleteScript = @"
        local current = redis.call('GET', KEYS[1])
        if not current then return 0 end
        redis.call('DEL', KEYS[1])
        redis.call('SREM', KEYS[2], ARGV[1])
        return 1";

    public RedisDslFlowStore(IConnectionMultiplexer redis, IMessageSerializer serializer, string prefix = "dslflow:")
    {
        _redis = redis;
        _serializer = serializer;
        _prefix = prefix;
    }

    public async Task<bool> CreateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        var db = _redis.GetDatabase();
        var key = _prefix + snapshot.FlowId;
        var indexKey = _prefix + "index";

        var data = _serializer.Serialize(new StoredSnapshot<TState>(snapshot));

        var result = await db.ScriptEvaluateAsync(CreateScript,
            [key, indexKey],
            [data, snapshot.FlowId]);

        return (long)result! == 1;
    }

    public async Task<FlowSnapshot<TState>?> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(string flowId, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        var db = _redis.GetDatabase();
        var key = _prefix + flowId;

        var data = await db.StringGetAsync(key);
        if (data.IsNullOrEmpty) return null;

        var stored = _serializer.Deserialize<StoredSnapshot<TState>>((byte[])data!);
        return stored?.ToSnapshot();
    }

    public async Task<bool> UpdateAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default)
        where TState : class, IFlowState
    {
        var db = _redis.GetDatabase();
        var key = _prefix + snapshot.FlowId;

        var newSnapshot = snapshot with { Version = snapshot.Version + 1, UpdatedAt = DateTime.UtcNow };
        var data = _serializer.Serialize(new StoredSnapshot<TState>(newSnapshot));

        var result = await db.ScriptEvaluateAsync(UpdateScript,
            [key],
            [snapshot.Version.ToString(), data]);

        return (long)result! == 1;
    }

    public async Task<bool> DeleteAsync(string flowId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _prefix + flowId;
        var indexKey = _prefix + "index";

        var result = await db.ScriptEvaluateAsync(DeleteScript,
            [key, indexKey],
            [flowId]);

        return (long)result! == 1;
    }

    public async Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _prefix + "wait:" + correlationId;
        var indexKey = _prefix + "wait:index";

        var data = _serializer.Serialize(condition);
        await db.StringSetAsync(key, data);
        var timeoutAt = condition.CreatedAt.Add(condition.Timeout);
        await db.SortedSetAddAsync(indexKey, correlationId, new DateTimeOffset(timeoutAt).ToUnixTimeMilliseconds());
    }

    public async Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _prefix + "wait:" + correlationId;

        var data = await db.StringGetAsync(key);
        if (data.IsNullOrEmpty) return null;

        return _serializer.Deserialize<WaitCondition>((byte[])data!);
    }

    public async Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _prefix + "wait:" + correlationId;

        var data = _serializer.Serialize(condition);
        await db.StringSetAsync(key, data);
    }

    public async Task ClearWaitConditionAsync(string correlationId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _prefix + "wait:" + correlationId;
        var indexKey = _prefix + "wait:index";

        await db.KeyDeleteAsync(key);
        await db.SortedSetRemoveAsync(indexKey, correlationId);
    }

    public async Task<IReadOnlyList<WaitCondition>> GetTimedOutWaitConditionsAsync(CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var indexKey = _prefix + "wait:index";
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Get all wait conditions with timeout <= now
        var timedOutIds = await db.SortedSetRangeByScoreAsync(indexKey, 0, nowMs);

        var results = new List<WaitCondition>();
        foreach (var id in timedOutIds)
        {
            var condition = await GetWaitConditionAsync(id!, ct);
            if (condition != null)
                results.Add(condition);
        }

        return results;
    }

    // Internal storage format
    private record StoredSnapshot<TState>(
        string FlowId,
        TState State,
        int CurrentStep,
        DslFlowStatus Status,
        string? Error,
        string? WaitConditionId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        int Version) where TState : class, IFlowState
    {
        public StoredSnapshot(FlowSnapshot<TState> snapshot)
            : this(snapshot.FlowId, snapshot.State, snapshot.CurrentStep, snapshot.Status,
                   snapshot.Error, snapshot.WaitCondition?.CorrelationId, snapshot.CreatedAt,
                   snapshot.UpdatedAt, snapshot.Version)
        { }

        public FlowSnapshot<TState> ToSnapshot() => new(
            FlowId, State, CurrentStep, Status, Error, null, CreatedAt, UpdatedAt, Version);
    }
}
