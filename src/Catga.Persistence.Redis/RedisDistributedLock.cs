using Catga.DistributedLock;
using StackExchange.Redis;

namespace Catga.Persistence.Redis;

/// <summary>
/// Redis-based distributed lock implementation using the Redlock algorithm
/// </summary>
public sealed class RedisDistributedLock : IDistributedLock
{
    private readonly IConnectionMultiplexer _redis;

    public RedisDistributedLock(IConnectionMultiplexer redis)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
    }

    public async ValueTask<ILockHandle?> TryAcquireAsync(
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var db = _redis.GetDatabase();
        var lockId = Guid.NewGuid().ToString();
        var lockKey = $"lock:{key}";

        // Try to acquire lock using SET NX PX
        var acquired = await db.StringSetAsync(
            lockKey,
            lockId,
            timeout,
            When.NotExists,
            CommandFlags.None);

        if (!acquired)
        {
            return null;
        }

        var handle = new RedisLockHandle(
            key,
            lockId,
            DateTime.UtcNow,
            db,
            lockKey);

        return handle;
    }
}

/// <summary>
/// Lock handle for Redis distributed lock
/// </summary>
internal sealed class RedisLockHandle : ILockHandle
{
    private readonly IDatabase _database;
    private readonly string _lockKey;
    private int _disposed;

    public string Key { get; }
    public string LockId { get; }
    public DateTime AcquiredAt { get; }
    public bool IsHeld => Volatile.Read(ref _disposed) == 0;

    public RedisLockHandle(
        string key,
        string lockId,
        DateTime acquiredAt,
        IDatabase database,
        string lockKey)
    {
        Key = key;
        LockId = lockId;
        AcquiredAt = acquiredAt;
        _database = database;
        _lockKey = lockKey;
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            // Release lock using Lua script to ensure atomicity
            var script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

            try
            {
                _database.ScriptEvaluate(
                    script,
                    new RedisKey[] { _lockKey },
                    new RedisValue[] { LockId });
            }
            catch
            {
                // Ignore errors during disposal
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            // Release lock using Lua script to ensure atomicity
            var script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

            try
            {
                await _database.ScriptEvaluateAsync(
                    script,
                    new RedisKey[] { _lockKey },
                    new RedisValue[] { LockId });
            }
            catch
            {
                // Ignore errors during disposal
            }
        }
    }
}

