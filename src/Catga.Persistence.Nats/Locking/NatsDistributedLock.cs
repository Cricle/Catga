using Catga.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace Catga.Persistence.Nats.Locking;

/// <summary>
/// NATS KV-based distributed lock using atomic operations.
/// </summary>
public sealed class NatsDistributedLock : IDistributedLock
{
    private readonly INatsConnection _nats;
    private readonly DistributedLockOptions _options;
    private readonly ILogger<NatsDistributedLock> _logger;
    private readonly string _bucketName;
    private INatsKVContext? _kv;
    private INatsKVStore? _store;

    public NatsDistributedLock(
        INatsConnection nats,
        IOptions<DistributedLockOptions> options,
        ILogger<NatsDistributedLock> logger,
        string bucketName = "locks")
    {
        _nats = nats;
        _options = options.Value;
        _logger = logger;
        _bucketName = bucketName;
    }

    private async ValueTask EnsureStoreAsync(CancellationToken ct)
    {
        if (_store != null) return;

        _kv = new NatsKVContext(new NatsJSContext(_nats));
        try
        {
            _store = await _kv.GetStoreAsync(_bucketName, ct);
        }
        catch
        {
            _store = await _kv.CreateStoreAsync(new NatsKVConfig(_bucketName)
            {
                History = 1,
                Storage = NatsKVStorageType.Memory,
                MaxAge = TimeSpan.FromMinutes(5) // Auto-cleanup expired locks
            }, ct);
        }
    }

    public async ValueTask<ILockHandle?> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var lockId = Guid.NewGuid().ToString("N");
        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
        var lockData = new LockData(lockId, expiresAt.UtcTicks);

        try
        {
            // Try to create - fails if key exists
            await _store!.CreateAsync(resource, System.Text.Encoding.UTF8.GetBytes(
                $"{lockId}|{expiresAt.UtcTicks}"), cancellationToken: ct);

            _logger.LogDebug("Lock acquired: {Resource} -> {LockId}", resource, lockId);
            return new NatsLockHandle(this, _store, resource, lockId, expiresAt);
        }
        catch (NatsKVCreateException)
        {
            // Key exists, check if expired
            try
            {
                var entry = await _store!.GetEntryAsync<byte[]>(resource, cancellationToken: ct);
                if (entry.Value != null)
                {
                    var data = ParseLockData(entry.Value);
                    if (data.ExpiresAtTicks < DateTimeOffset.UtcNow.Ticks)
                    {
                        // Lock expired, try to take over
                        await _store.UpdateAsync(resource, System.Text.Encoding.UTF8.GetBytes(
                            $"{lockId}|{expiresAt.UtcTicks}"), entry.Revision, cancellationToken: ct);

                        _logger.LogDebug("Lock acquired (expired takeover): {Resource} -> {LockId}", resource, lockId);
                        return new NatsLockHandle(this, _store, resource, lockId, expiresAt);
                    }
                }
            }
            catch
            {
                // Failed to take over, lock is held
            }

            return null;
        }
    }

    public async ValueTask<ILockHandle> AcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan waitTimeout,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + waitTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var handle = await TryAcquireAsync(resource, expiry, ct);
            if (handle != null)
                return handle;

            await Task.Delay(_options.RetryInterval, ct);
        }

        throw new LockAcquisitionException(resource, waitTimeout);
    }

    public async ValueTask<bool> IsLockedAsync(string resource, CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        try
        {
            var entry = await _store!.GetEntryAsync<byte[]>(resource, cancellationToken: ct);
            if (entry.Value == null) return false;

            var data = ParseLockData(entry.Value);
            return data.ExpiresAtTicks > DateTimeOffset.UtcNow.Ticks;
        }
        catch
        {
            return false;
        }
    }

    private static LockData ParseLockData(byte[] data)
    {
        var str = System.Text.Encoding.UTF8.GetString(data);
        var parts = str.Split('|');
        return new LockData(parts[0], long.Parse(parts[1]));
    }

    private record struct LockData(string LockId, long ExpiresAtTicks);
}

internal sealed class NatsLockHandle : ILockHandle
{
    private readonly NatsDistributedLock _parent;
    private readonly INatsKVStore _store;
    private bool _disposed;

    public string Resource { get; }
    public string LockId { get; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsValid => !_disposed && ExpiresAt > DateTimeOffset.UtcNow;

    public NatsLockHandle(
        NatsDistributedLock parent,
        INatsKVStore store,
        string resource,
        string lockId,
        DateTimeOffset expiresAt)
    {
        _parent = parent;
        _store = store;
        Resource = resource;
        LockId = lockId;
        ExpiresAt = expiresAt;
    }

    public async ValueTask ExtendAsync(TimeSpan extension, CancellationToken ct = default)
    {
        if (_disposed)
            throw new LockLostException(Resource, LockId);

        var newExpiry = DateTimeOffset.UtcNow.Add(extension);

        try
        {
            var entry = await _store.GetEntryAsync<byte[]>(Resource, cancellationToken: ct);
            if (entry.Value == null)
                throw new LockLostException(Resource, LockId);

            var str = System.Text.Encoding.UTF8.GetString(entry.Value);
            if (!str.StartsWith(LockId))
                throw new LockLostException(Resource, LockId);

            await _store.UpdateAsync(Resource, System.Text.Encoding.UTF8.GetBytes(
                $"{LockId}|{newExpiry.UtcTicks}"), entry.Revision, cancellationToken: ct);

            ExpiresAt = newExpiry;
        }
        catch (Exception ex) when (ex is not LockLostException)
        {
            throw new LockLostException(Resource, LockId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            var entry = await _store.GetEntryAsync<byte[]>(Resource);
            if (entry.Value != null)
            {
                var str = System.Text.Encoding.UTF8.GetString(entry.Value);
                if (str.StartsWith(LockId))
                {
                    await _store.DeleteAsync(Resource);
                }
            }
        }
        catch
        {
            // Ignore errors during release
        }
    }
}
