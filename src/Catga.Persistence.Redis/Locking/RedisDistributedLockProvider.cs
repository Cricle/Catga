using Catga.Abstractions;
using Catga.Locking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.Locking;

/// <summary>
/// Redis-based distributed lock provider implementing IDistributedLockProvider.
/// Wraps RedisDistributedLock for attribute-driven behavior support.
/// </summary>
public sealed class RedisDistributedLockProvider : IDistributedLockProvider
{
    private readonly RedisDistributedLock _lock;

    public RedisDistributedLockProvider(
        IConnectionMultiplexer redis,
        IOptions<DistributedLockOptions> options,
        ILogger<RedisDistributedLock> logger)
    {
        _lock = new RedisDistributedLock(redis, options, logger);
    }

    public async ValueTask<IAsyncDisposable?> AcquireAsync(
        string key,
        TimeSpan timeout,
        TimeSpan wait,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var handle = await _lock.AcquireAsync(key, timeout, wait, cancellationToken);
            return new LockHandleWrapper(handle);
        }
        catch (LockAcquisitionException)
        {
            return null;
        }
    }

    private sealed class LockHandleWrapper(Abstractions.ILockHandle handle) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => handle.DisposeAsync();
    }
}
