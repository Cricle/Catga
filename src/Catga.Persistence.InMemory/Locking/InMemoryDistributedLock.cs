using System.Collections.Concurrent;
using Catga.Abstractions;
using DotNext.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catga.Persistence.InMemory.Locking;

/// <summary>In-memory distributed lock using DotNext.Threading for single-node or testing.</summary>
public sealed partial class InMemoryDistributedLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, LockState> _locks = new();
    private readonly DistributedLockOptions _opts;
    private readonly ILogger<InMemoryDistributedLock> _logger;

    public InMemoryDistributedLock(IOptions<DistributedLockOptions> options, ILogger<InMemoryDistributedLock> logger)
    {
        _opts = options.Value;
        _logger = logger;
    }

    private LockState GetOrCreateLock(string resource)
    {
        return _locks.GetOrAdd(resource, _ => new LockState());
    }

    public async ValueTask<ILockHandle?> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        var state = GetOrCreateLock(resource);
        if (await state.Lock.TryAcquireAsync(TimeSpan.Zero, ct))
        {
            var lockId = GenerateLockId();
            var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
            state.LockId = lockId;
            state.ExpiresAt = expiresAt;
            LogLockAcquired(_logger, resource, lockId, expiry.TotalSeconds);
            return new InMemoryLockHandle(this, state, resource, lockId, expiresAt);
        }
        return null;
    }

    public async ValueTask<ILockHandle> AcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan waitTimeout,
        CancellationToken ct = default)
    {
        var state = GetOrCreateLock(resource);
        if (await state.Lock.TryAcquireAsync(waitTimeout, ct))
        {
            var lockId = GenerateLockId();
            var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
            state.LockId = lockId;
            state.ExpiresAt = expiresAt;
            LogLockAcquired(_logger, resource, lockId, expiry.TotalSeconds);
            return new InMemoryLockHandle(this, state, resource, lockId, expiresAt);
        }

        LogLockTimeout(_logger, resource, waitTimeout.TotalSeconds);
        throw new LockAcquisitionException(resource, waitTimeout);
    }

    public ValueTask<bool> IsLockedAsync(string resource, CancellationToken ct = default)
    {
        if (_locks.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(state.Lock.IsLockHeld && state.ExpiresAt > DateTimeOffset.UtcNow);
        }

        return ValueTask.FromResult(false);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lock acquisition timeout: {Resource} after {TimeoutSeconds}s")]
    private static partial void LogLockTimeout(ILogger logger, string resource, double timeoutSeconds);

    #endregion

    private sealed class LockState
    {
        public AsyncExclusiveLock Lock { get; } = new();
        public string? LockId { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed class InMemoryLockHandle : ILockHandle
    {
        private readonly InMemoryDistributedLock _parent;
        private readonly LockState _state;
        private int _disposed;

        public string Resource { get; }
        public string LockId { get; }
        public DateTimeOffset ExpiresAt { get; private set; }
        public bool IsValid => _disposed == 0 && DateTimeOffset.UtcNow < ExpiresAt;

        public InMemoryLockHandle(InMemoryDistributedLock parent, LockState state, string resource, string lockId, DateTimeOffset expiresAt)
        {
            _parent = parent;
            _state = state;
            Resource = resource;
            LockId = lockId;
            ExpiresAt = expiresAt;
        }

        public ValueTask ExtendAsync(TimeSpan extension, CancellationToken ct = default)
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(InMemoryLockHandle));
            }

            if (!IsValid)
            {
                throw new LockLostException(Resource, LockId);
            }

            ExpiresAt = DateTimeOffset.UtcNow.Add(extension);
            _state.ExpiresAt = ExpiresAt;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _state.LockId = null;
                _state.Lock.Release();
                LogLockReleased(_parent._logger, Resource, LockId);
            }
            return ValueTask.CompletedTask;
        }
    }
}
