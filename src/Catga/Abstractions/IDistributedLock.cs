namespace Catga.DistributedLock;

/// <summary>
/// Distributed lock abstraction for coordinating access to shared resources across multiple processes or machines
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Try to acquire a distributed lock
    /// </summary>
    /// <param name="key">Unique key for the lock</param>
    /// <param name="timeout">How long to wait before lock expires</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Lock handle if acquired, null if failed to acquire</returns>
    public ValueTask<ILockHandle?> TryAcquireAsync(
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Handle for an acquired distributed lock
/// </summary>
public interface ILockHandle : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// The key of the lock
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// When the lock was acquired
    /// </summary>
    public DateTime AcquiredAt { get; }

    /// <summary>
    /// Whether the lock is still held
    /// </summary>
    public bool IsHeld { get; }

    /// <summary>
    /// Unique identifier for this lock instance
    /// </summary>
    public string LockId { get; }
}

