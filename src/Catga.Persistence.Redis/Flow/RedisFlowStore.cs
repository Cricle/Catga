using Catga.Flow;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Flow;

/// <summary>
/// Redis flow store. Lock-free with Lua atomic scripts.
/// Production-ready for distributed clusters.
/// </summary>
public sealed class RedisFlowStore : IFlowStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _prefix;

    // Lua scripts for atomic operations
    private const string CreateScript = @"
        if redis.call('EXISTS', KEYS[1]) == 1 then return 0 end
        redis.call('HSET', KEYS[1],
            'type', ARGV[1], 'status', ARGV[2], 'step', ARGV[3],
            'version', ARGV[4], 'owner', ARGV[5], 'heartbeat', ARGV[6],
            'data', ARGV[7], 'error', ARGV[8])
        redis.call('SADD', KEYS[2], KEYS[1])
        return 1";

    private const string UpdateScript = @"
        local v = redis.call('HGET', KEYS[1], 'version')
        if v ~= ARGV[1] then return 0 end
        redis.call('HSET', KEYS[1],
            'status', ARGV[2], 'step', ARGV[3], 'version', ARGV[4],
            'owner', ARGV[5], 'heartbeat', ARGV[6], 'error', ARGV[7], 'data', ARGV[8])
        return 1";

    private const string HeartbeatScript = @"
        local o = redis.call('HGET', KEYS[1], 'owner')
        if o ~= ARGV[1] then return 0 end
        local v = redis.call('HGET', KEYS[1], 'version')
        if v ~= ARGV[2] then return 0 end
        redis.call('HSET', KEYS[1], 'heartbeat', ARGV[3], 'version', ARGV[4])
        return 1";

    private const string ClaimScript = @"
        local ids = redis.call('SMEMBERS', KEYS[1])
        local now = tonumber(ARGV[1])
        local timeout = tonumber(ARGV[2])
        for _, id in ipairs(ids) do
            local status = redis.call('HGET', id, 'status')
            if status ~= '2' and status ~= '3' then
                local hb = tonumber(redis.call('HGET', id, 'heartbeat') or '0')
                if now - hb >= timeout then
                    local v = tonumber(redis.call('HGET', id, 'version') or '0')
                    redis.call('HSET', id, 'owner', ARGV[3], 'heartbeat', ARGV[1], 'version', v + 1)
                    return id
                end
            end
        end
        return nil";

    public RedisFlowStore(IConnectionMultiplexer redis, string prefix = "flow:")
    {
        _redis = redis;
        _prefix = prefix;
    }

    public async ValueTask<bool> CreateAsync(FlowState state, CancellationToken ct = default)
    {
        // Validate input
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Id, nameof(state.Id));
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Type, nameof(state.Type));
        
        if (state.Id.Length > 256)
            throw new ArgumentException("Flow ID too long (max 256 characters)", nameof(state.Id));
        if (state.Type.Length > 256)
            throw new ArgumentException("Flow Type too long (max 256 characters)", nameof(state.Type));
        if (state.Owner != null && state.Owner.Length > 256)
            throw new ArgumentException("Owner too long (max 256 characters)", nameof(state.Owner));
        if (state.Error != null && state.Error.Length > 4096)
            throw new ArgumentException("Error message too long (max 4096 characters)", nameof(state.Error));
        if (state.Data != null && state.Data.Length > 1024 * 1024)
            throw new ArgumentException("Data too large (max 1MB)", nameof(state.Data));

        var db = _redis.GetDatabase();
        var key = _prefix + state.Id;
        var typeKey = _prefix + "type:" + state.Type;

        var result = await db.ScriptEvaluateAsync(CreateScript,
            [key, typeKey],
            [
                state.Type,
                ((int)state.Status).ToString(),
                state.Step.ToString(),
                state.Version.ToString(),
                state.Owner ?? "",
                state.HeartbeatAt.ToString(),
                state.Data != null ? Convert.ToBase64String(state.Data) : "",
                state.Error ?? ""
            ]);

        return (long)result! == 1;
    }

    public async ValueTask<bool> UpdateAsync(FlowState state, CancellationToken ct = default)
    {
        // Validate input
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Id, nameof(state.Id));
        
        if (state.Owner != null && state.Owner.Length > 256)
            throw new ArgumentException("Owner too long (max 256 characters)", nameof(state.Owner));
        if (state.Error != null && state.Error.Length > 4096)
            throw new ArgumentException("Error message too long (max 4096 characters)", nameof(state.Error));
        if (state.Data != null && state.Data.Length > 1024 * 1024)
            throw new ArgumentException("Data too large (max 1MB)", nameof(state.Data));

        var db = _redis.GetDatabase();
        var key = _prefix + state.Id;
        var newVersion = state.Version + 1;

        var result = await db.ScriptEvaluateAsync(UpdateScript,
            [key],
            [
                state.Version.ToString(),
                ((int)state.Status).ToString(),
                state.Step.ToString(),
                newVersion.ToString(),
                state.Owner ?? "",
                state.HeartbeatAt.ToString(),
                state.Error ?? "",
                state.Data != null ? Convert.ToBase64String(state.Data) : ""
            ]);

        if ((long)result! == 1)
        {
            state.Version = newVersion;
            return true;
        }
        return false;
    }

    public async ValueTask<FlowState?> GetAsync(string id, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _prefix + id;
        var hash = await db.HashGetAllAsync(key);

        if (hash.Length == 0) return null;

        var dict = hash.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

        return new FlowState
        {
            Id = id,
            Type = dict.GetValueOrDefault("type", ""),
            Status = (FlowStatus)int.Parse(dict.GetValueOrDefault("status", "0")),
            Step = int.Parse(dict.GetValueOrDefault("step", "0")),
            Version = long.Parse(dict.GetValueOrDefault("version", "0")),
            Owner = dict.GetValueOrDefault("owner"),
            HeartbeatAt = long.Parse(dict.GetValueOrDefault("heartbeat", "0")),
            Data = dict.TryGetValue("data", out var data) && !string.IsNullOrEmpty(data)
                ? Convert.FromBase64String(data) : null,
            Error = dict.GetValueOrDefault("error")
        };
    }

    public async ValueTask<FlowState?> TryClaimAsync(string type, string owner, long timeoutMs, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var typeKey = _prefix + "type:" + type;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = await db.ScriptEvaluateAsync(ClaimScript,
            [typeKey],
            [nowMs.ToString(), timeoutMs.ToString(), owner]);

        if (result.IsNull) return null;

        var id = result.ToString()!.Replace(_prefix, "");
        return await GetAsync(id, ct);
    }

    public async ValueTask<bool> HeartbeatAsync(string id, string owner, long version, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = _prefix + id;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = await db.ScriptEvaluateAsync(HeartbeatScript,
            [key],
            [owner, version.ToString(), nowMs.ToString(), (version + 1).ToString()]);

        return (long)result! == 1;
    }
}
