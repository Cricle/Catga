namespace Catga.Abstractions;

/// <summary>
/// Distributed lock abstraction for cluster coordination.
/// AOT-compatible, zero-allocation design.
/// </summary>
public interface IDistributedLock
{
    /// <summary>Try to acquire lock without waiting. Returns null if lock is held.</summary>
    ValueTask<ILockHandle?> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken ct = default);

    /// <summary>Acquire lock with wait timeout. Throws if timeout exceeded.</summary>
    ValueTask<ILockHandle> AcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan waitTimeout,
        CancellationToken ct = default);

    /// <summary>Check if resource is currently locked.</summary>
    ValueTask<bool> IsLockedAsync(string resource, CancellationToken ct = default);
}

/// <summary>Lock handle - dispose to release lock.</summary>
public interface ILockHandle : IAsyncDisposable
{
    /// <summary>Resource name being locked.</summary>
    string Resource { get; }

    /// <summary>Unique lock identifier.</summary>
    string LockId { get; }

    /// <summary>When the lock expires.</summary>
    DateTimeOffset ExpiresAt { get; }

    /// <summary>Whether the lock is still valid.</summary>
    bool IsValid { get; }

    /// <summary>Extend lock expiry. Throws if lock expired or lost.</summary>
    ValueTask ExtendAsync(TimeSpan extension, CancellationToken ct = default);
}

/// <summary>Distributed lock options.</summary>
public sealed class DistributedLockOptions
{
    /// <summary>Default lock expiry if not specified.</summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Default wait timeout if not specified.</summary>
    public TimeSpan DefaultWaitTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Retry interval when waiting for lock.</summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Enable automatic lock extension (watchdog).</summary>
    public bool EnableAutoExtend { get; set; } = false;

    /// <summary>Auto-extend interval (should be less than expiry).</summary>
    public TimeSpan AutoExtendInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Key prefix for lock keys.</summary>
    public string KeyPrefix { get; set; } = "catga:lock:";
}

/// <summary>Exception thrown when lock acquisition fails.</summary>
public sealed class LockAcquisitionException : Exception
{
    public string Resource { get; }
    public TimeSpan WaitTimeout { get; }

    public LockAcquisitionException(string resource, TimeSpan waitTimeout)
        : base($"Failed to acquire lock on '{resource}' within {waitTimeout.TotalSeconds:F1}s")
    {
        Resource = resource;
        WaitTimeout = waitTimeout;
    }
}

/// <summary>Exception thrown when lock operation fails because lock was lost.</summary>
public sealed class LockLostException : Exception
{
    public string Resource { get; }
    public string LockId { get; }

    public LockLostException(string resource, string lockId)
        : base($"Lock on '{resource}' (id: {lockId}) was lost or expired")
    {
        Resource = resource;
        LockId = lockId;
    }
}
