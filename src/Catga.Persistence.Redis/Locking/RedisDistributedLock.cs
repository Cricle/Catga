using System.Buffers;
using Catga.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Locking;

/// <summary>
/// Redis-based distributed lock using SET NX with expiry.
/// AOT-compatible, low-allocation implementation.
/// </summary>
public sealed partial class RedisDistributedLock : IDistributedLock
{
    private readonly IConnectionMultiplexer _redis;
    private readonly DistributedLockOptions _options;
    private readonly ILogger<RedisDistributedLock> _logger;

    // Lua script for atomic lock release (only release if we own it)
    private const string ReleaseLockScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
        """;

    // Lua script for atomic lock extension
    private const string ExtendLockScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('pexpire', KEYS[1], ARGV[2])
        else
            return 0
        end
        """;

    public RedisDistributedLock(
        IConnectionMultiplexer redis,
        IOptions<DistributedLockOptions> options,
        ILogger<RedisDistributedLock> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<ILockHandle?> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
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
            LogLockAcquired(_logger, resource, lockId, expiry.TotalSeconds);
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
        var deadline = DateTimeOffset.UtcNow.Add(waitTimeout);
        var retryInterval = _options.RetryInterval;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var handle = await TryAcquireAsync(resource, expiry, ct);
            if (handle != null)
                return handle;

            // Wait before retry
            var remaining = deadline - DateTimeOffset.UtcNow;
            var delay = remaining < retryInterval ? remaining : retryInterval;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
        }

        LogLockTimeout(_logger, resource, waitTimeout.TotalSeconds);
        throw new LockAcquisitionException(resource, waitTimeout);
    }

    public async ValueTask<bool> IsLockedAsync(string resource, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = GetKey(resource);
        return await db.KeyExistsAsync(key);
    }

    private string GetKey(string resource) => string.Concat(_options.KeyPrefix, resource);

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
                ReleaseLockScript,
                [key],
                [lockId]);

            if ((long)result! == 1)
                LogLockReleased(_logger, key, lockId);
            else
                LogLockAlreadyReleased(_logger, key, lockId);
        }
        catch (Exception ex)
        {
            LogLockReleaseError(_logger, key, lockId, ex);
        }
    }

    internal async ValueTask<bool> ExtendLockAsync(IDatabase db, string key, string lockId, TimeSpan extension)
    {
        var result = await db.ScriptEvaluateAsync(
            ExtendLockScript,
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
                throw new ObjectDisposedException(nameof(RedisLockHandle));

            if (!IsValid)
                throw new LockLostException(Resource, LockId);

            var success = await _parent.ExtendLockAsync(_db, _key, LockId, extension);
            if (!success)
                throw new LockLostException(Resource, LockId);

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
