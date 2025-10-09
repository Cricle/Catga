using System.Collections.Concurrent;

namespace Catga.DistributedLock;

/// <summary>
/// In-memory implementation of distributed lock (for single-instance scenarios or testing)
/// </summary>
public sealed class MemoryDistributedLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async ValueTask<ILockHandle?> TryAcquireAsync(
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        var acquired = await semaphore.WaitAsync(timeout, cancellationToken);

        if (!acquired)
        {
            return null;
        }

        var lockId = Guid.NewGuid().ToString();
        var handle = new MemoryLockHandle(
            key,
            lockId,
            DateTime.UtcNow,
            semaphore,
            () => CleanupLock(key, semaphore));

        return handle;
    }

    private void CleanupLock(string key, SemaphoreSlim semaphore)
    {
        // Only remove from dictionary if no one is waiting
        if (semaphore.CurrentCount == 1)
        {
            _locks.TryRemove(key, out _);
        }
    }
}

/// <summary>
/// Lock handle for in-memory distributed lock
/// </summary>
internal sealed class MemoryLockHandle : ILockHandle
{
    private readonly SemaphoreSlim _semaphore;
    private readonly Action _cleanup;
    private int _disposed;

    public string Key { get; }
    public string LockId { get; }
    public DateTime AcquiredAt { get; }
    public bool IsHeld => Volatile.Read(ref _disposed) == 0;

    public MemoryLockHandle(
        string key,
        string lockId,
        DateTime acquiredAt,
        SemaphoreSlim semaphore,
        Action cleanup)
    {
        Key = key;
        LockId = lockId;
        AcquiredAt = acquiredAt;
        _semaphore = semaphore;
        _cleanup = cleanup;
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            _semaphore.Release();
            _cleanup();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

