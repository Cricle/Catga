using System.Collections.Concurrent;
using Catga.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catga.Persistence.InMemory.Locking;

/// <summary>
/// In-memory distributed lock for single-node or testing scenarios.
/// Thread-safe, low-allocation implementation.
/// </summary>
public sealed partial class InMemoryDistributedLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new();
    private readonly DistributedLockOptions _options;
    private readonly ILogger<InMemoryDistributedLock> _logger;
    private readonly Timer _cleanupTimer;

    public InMemoryDistributedLock(
        IOptions<DistributedLockOptions> options,
        ILogger<InMemoryDistributedLock> logger)
    {
        _options = options.Value;
        _logger = logger;
        _cleanupTimer = new Timer(CleanupExpiredLocks, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public ValueTask<ILockHandle?> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        var lockId = GenerateLockId();
        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
        var entry = new LockEntry(lockId, expiresAt);

        // Try to add or check if existing lock expired
        while (true)
        {
            if (_locks.TryAdd(resource, entry))
            {
                LogLockAcquired(_logger, resource, lockId, expiry.TotalSeconds);
                return ValueTask.FromResult<ILockHandle?>(new InMemoryLockHandle(this, resource, lockId, expiresAt));
            }

            if (_locks.TryGetValue(resource, out var existing))
            {
                if (existing.ExpiresAt < DateTimeOffset.UtcNow)
                {
                    // Expired, try to replace
                    if (_locks.TryUpdate(resource, entry, existing))
                    {
                        LogLockAcquired(_logger, resource, lockId, expiry.TotalSeconds);
                        return ValueTask.FromResult<ILockHandle?>(new InMemoryLockHandle(this, resource, lockId, expiresAt));
                    }
                    continue; // Retry
                }
            }

            return ValueTask.FromResult<ILockHandle?>(null);
        }
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

            var remaining = deadline - DateTimeOffset.UtcNow;
            var delay = remaining < retryInterval ? remaining : retryInterval;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
        }

        LogLockTimeout(_logger, resource, waitTimeout.TotalSeconds);
        throw new LockAcquisitionException(resource, waitTimeout);
    }

    public ValueTask<bool> IsLockedAsync(string resource, CancellationToken ct = default)
    {
        if (_locks.TryGetValue(resource, out var entry))
        {
            return ValueTask.FromResult(entry.ExpiresAt > DateTimeOffset.UtcNow);
        }
        return ValueTask.FromResult(false);
    }

    internal bool TryRelease(string resource, string lockId)
    {
        if (_locks.TryGetValue(resource, out var entry) && entry.LockId == lockId)
        {
            if (_locks.TryRemove(resource, out _))
            {
                LogLockReleased(_logger, resource, lockId);
                return true;
            }
        }
        LogLockAlreadyReleased(_logger, resource, lockId);
        return false;
    }

    internal bool TryExtend(string resource, string lockId, TimeSpan extension)
    {
        if (_locks.TryGetValue(resource, out var entry) && entry.LockId == lockId)
        {
            var newExpiry = DateTimeOffset.UtcNow.Add(extension);
            var newEntry = new LockEntry(lockId, newExpiry);
            return _locks.TryUpdate(resource, newEntry, entry);
        }
        return false;
    }

    private void CleanupExpiredLocks(object? state)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _locks)
        {
            if (kvp.Value.ExpiresAt < now)
            {
                _locks.TryRemove(kvp.Key, out _);
            }
        }
    }

    private static string GenerateLockId()
    {
        Span<byte> buffer = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(buffer);
        return Convert.ToBase64String(buffer);
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock acquired: {Resource} (id: {LockId}, expiry: {ExpirySeconds}s)")]
    private static partial void LogLockAcquired(ILogger logger, string resource, string lockId, double expirySeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Lock released: {Resource} (id: {LockId})")]
    private static partial void LogLockReleased(ILogger logger, string resource, string lockId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lock already released or expired: {Resource} (id: {LockId})")]
    private static partial void LogLockAlreadyReleased(ILogger logger, string resource, string lockId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lock acquisition timeout: {Resource} after {TimeoutSeconds}s")]
    private static partial void LogLockTimeout(ILogger logger, string resource, double timeoutSeconds);

    #endregion

    private readonly record struct LockEntry(string LockId, DateTimeOffset ExpiresAt);

    private sealed class InMemoryLockHandle : ILockHandle
    {
        private readonly InMemoryDistributedLock _parent;
        private int _disposed;

        public string Resource { get; }
        public string LockId { get; }
        public DateTimeOffset ExpiresAt { get; private set; }
        public bool IsValid => _disposed == 0 && DateTimeOffset.UtcNow < ExpiresAt;

        public InMemoryLockHandle(InMemoryDistributedLock parent, string resource, string lockId, DateTimeOffset expiresAt)
        {
            _parent = parent;
            Resource = resource;
            LockId = lockId;
            ExpiresAt = expiresAt;
        }

        public ValueTask ExtendAsync(TimeSpan extension, CancellationToken ct = default)
        {
            if (_disposed != 0)
                throw new ObjectDisposedException(nameof(InMemoryLockHandle));

            if (!IsValid)
                throw new LockLostException(Resource, LockId);

            if (!_parent.TryExtend(Resource, LockId, extension))
                throw new LockLostException(Resource, LockId);

            ExpiresAt = DateTimeOffset.UtcNow.Add(extension);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _parent.TryRelease(Resource, LockId);
            }
            return ValueTask.CompletedTask;
        }
    }
}
