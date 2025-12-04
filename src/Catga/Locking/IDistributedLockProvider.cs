namespace Catga.Locking;

/// <summary>
/// Provides distributed locking capabilities.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// Acquires a distributed lock.
    /// </summary>
    /// <param name="key">Lock key</param>
    /// <param name="timeout">How long the lock is held</param>
    /// <param name="wait">How long to wait for lock acquisition</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Lock handle, or null if lock could not be acquired</returns>
    ValueTask<IAsyncDisposable?> AcquireAsync(
        string key,
        TimeSpan timeout,
        TimeSpan wait,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory distributed lock provider for development/testing.
/// </summary>
public sealed class InMemoryDistributedLockProvider : IDistributedLockProvider
{
    private readonly SemaphoreSlim _globalLock = new(1, 1);
    private readonly Dictionary<string, SemaphoreSlim> _locks = new();

    public async ValueTask<IAsyncDisposable?> AcquireAsync(
        string key,
        TimeSpan timeout,
        TimeSpan wait,
        CancellationToken cancellationToken = default)
    {
        SemaphoreSlim lockObj;

        await _globalLock.WaitAsync(cancellationToken);
        try
        {
            if (!_locks.TryGetValue(key, out lockObj!))
            {
                lockObj = new SemaphoreSlim(1, 1);
                _locks[key] = lockObj;
            }
        }
        finally
        {
            _globalLock.Release();
        }

        if (await lockObj.WaitAsync(wait, cancellationToken))
        {
            return new LockHandle(lockObj);
        }

        return null;
    }

    private sealed class LockHandle : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public LockHandle(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _semaphore.Release();
            }
            return ValueTask.CompletedTask;
        }
    }
}
