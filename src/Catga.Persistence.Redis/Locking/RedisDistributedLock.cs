using Catga.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Locking;

/// <summary>Redis-based distributed lock using Polly for retry logic.</summary>
public sealed partial class RedisDistributedLock(IConnectionMultiplexer redis, IOptions<DistributedLockOptions> options, ILogger<RedisDistributedLock> logger) : IDistributedLock
{
    private readonly DistributedLockOptions _opts = options.Value;
    private const string ReleaseScript = "if redis.call('get',KEYS[1])==ARGV[1] then return redis.call('del',KEYS[1]) else return 0 end";
    private const string ExtendScript = "if redis.call('get',KEYS[1])==ARGV[1] then return redis.call('pexpire',KEYS[1],ARGV[2]) else return 0 end";

    public async ValueTask<ILockHandle?> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = GetKey(resource);
        var lockId = GenerateLockId();
        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);

        // SET key value NX PX milliseconds
        var acquired = await db.StringSetAsync(
            key,
            lockId,
            expiry,
            When.NotExists,
            CommandFlags.DemandMaster);

        if (acquired)
        {
            LogLockAcquired(logger, resource, lockId, expiry.TotalSeconds);
            return new RedisLockHandle(this, db, key, resource, lockId, expiresAt);
        }

        return null;
    }

    public async ValueTask<ILockHandle> AcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan waitTimeout,
        CancellationToken ct = default)
    {
        var retryInterval = _opts.RetryInterval;
        var maxRetries = (int)(waitTimeout.TotalMilliseconds / retryInterval.TotalMilliseconds);

        var pipeline = new ResiliencePipelineBuilder<ILockHandle?>()
            .AddRetry(new RetryStrategyOptions<ILockHandle?>
            {
                MaxRetryAttempts = Math.Max(1, maxRetries),
                Delay = retryInterval,
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder<ILockHandle?>().HandleResult(h => h is null)
            })
            .Build();

        var handle = await pipeline.ExecuteAsync(async c => await TryAcquireAsync(resource, expiry, c), ct);

        if (handle is null)
        {
            LogLockTimeout(logger, resource, waitTimeout.TotalSeconds);
            throw new LockAcquisitionException(resource, waitTimeout);
        }

        return handle;
    }

    public async ValueTask<bool> IsLockedAsync(string resource, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = GetKey(resource);
        return await db.KeyExistsAsync(key);
    }

    private string GetKey(string resource) => string.Concat(_opts.KeyPrefix, resource);

    private static string GenerateLockId()
    {
        // Use stack-allocated buffer for GUID
        Span<byte> buffer = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(buffer);
        return Convert.ToBase64String(buffer);
    }

    internal async ValueTask ReleaseLockAsync(IDatabase db, string key, string lockId)
    {
        try
        {
            var result = await db.ScriptEvaluateAsync(
                ReleaseScript,
                [key],
                [lockId]);

            if ((long)result! == 1)
            {
                LogLockReleased(logger, key, lockId);
            }
            else
            {
                LogLockAlreadyReleased(logger, key, lockId);
            }
        }
        catch (Exception ex)
        {
            LogLockReleaseError(logger, key, lockId, ex);
        }
    }

    internal async ValueTask<bool> ExtendLockAsync(IDatabase db, string key, string lockId, TimeSpan extension)
    {
        var result = await db.ScriptEvaluateAsync(
            ExtendScript,
            [key],
            [lockId, (long)extension.TotalMilliseconds]);

        return (long)result! == 1;
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock acquired: {Resource} (id: {LockId}, expiry: {ExpirySeconds}s)")]
    private static partial void LogLockAcquired(ILogger logger, string resource, string lockId, double expirySeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock released: {Key} (id: {LockId})")]
    private static partial void LogLockReleased(ILogger logger, string key, string lockId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lock already released or expired: {Key} (id: {LockId})")]
    private static partial void LogLockAlreadyReleased(ILogger logger, string key, string lockId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error releasing lock: {Key} (id: {LockId})")]
    private static partial void LogLockReleaseError(ILogger logger, string key, string lockId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lock acquisition timeout: {Resource} after {TimeoutSeconds}s")]
    private static partial void LogLockTimeout(ILogger logger, string resource, double timeoutSeconds);

    #endregion

    /// <summary>Redis lock handle implementation.</summary>
    private sealed class RedisLockHandle : ILockHandle
    {
        private readonly RedisDistributedLock _parent;
        private readonly IDatabase _db;
        private readonly string _key;
        private int _disposed;

        public string Resource { get; }
        public string LockId { get; }
        public DateTimeOffset ExpiresAt { get; private set; }
        public bool IsValid => _disposed == 0 && DateTimeOffset.UtcNow < ExpiresAt;

        public RedisLockHandle(
            RedisDistributedLock parent,
            IDatabase db,
            string key,
            string resource,
            string lockId,
            DateTimeOffset expiresAt)
        {
            _parent = parent;
            _db = db;
            _key = key;
            Resource = resource;
            LockId = lockId;
            ExpiresAt = expiresAt;
        }

        public async ValueTask ExtendAsync(TimeSpan extension, CancellationToken ct = default)
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(RedisLockHandle));
            }

            if (!IsValid)
            {
                throw new LockLostException(Resource, LockId);
            }

            var success = await _parent.ExtendLockAsync(_db, _key, LockId, extension);
            if (!success)
            {
                throw new LockLostException(Resource, LockId);
            }

            ExpiresAt = DateTimeOffset.UtcNow.Add(extension);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await _parent.ReleaseLockAsync(_db, _key, LockId);
            }
        }
    }
}
