using Catga.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace Catga.Persistence.Nats.RateLimiting;

/// <summary>
/// NATS KV-based distributed rate limiter using sliding window algorithm.
/// </summary>
public sealed class NatsRateLimiter : IDistributedRateLimiter
{
    private readonly INatsConnection _nats;
    private readonly DistributedRateLimiterOptions _options;
    private readonly string _bucketName;
    private INatsKVContext? _kv;
    private INatsKVStore? _store;

    public NatsRateLimiter(
        INatsConnection nats,
        IOptions<DistributedRateLimiterOptions> options,
        string bucketName = "ratelimit")
    {
        _nats = nats;
        _options = options.Value;
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
                MaxAge = _options.DefaultWindow * 2 // Auto-cleanup old entries
            }, ct);
        }
    }

    public async ValueTask<RateLimitResult> TryAcquireAsync(
        string key,
        int permits = 1,
        CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var windowKey = GetWindowKey(key, now);

        try
        {
            // Try to get current count
            long currentCount = 0;
            ulong revision = 0;

            try
            {
                var entry = await _store!.GetEntryAsync<byte[]>(windowKey, cancellationToken: ct);
                if (entry.Value != null)
                {
                    currentCount = BitConverter.ToInt64(entry.Value);
                    revision = entry.Revision;
                }
            }
            catch (NatsKVKeyNotFoundException)
            {
                // Key doesn't exist, start fresh
            }

            // Check limit
            if (currentCount + permits > _options.DefaultPermitLimit)
            {
                var resetAfter = GetResetTime(now);
                return RateLimitResult.Rejected(RateLimitRejectionReason.RateLimitExceeded, resetAfter);
            }

            // Increment count
            var newCount = currentCount + permits;
            var data = BitConverter.GetBytes(newCount);

            if (revision == 0)
            {
                await _store!.CreateAsync(windowKey, data, cancellationToken: ct);
            }
            else
            {
                await _store!.UpdateAsync(windowKey, data, revision, cancellationToken: ct);
            }

            return RateLimitResult.Acquired(_options.DefaultPermitLimit - newCount);
        }
        catch (NatsKVWrongLastRevisionException)
        {
            // Concurrent update, retry once
            return await TryAcquireAsync(key, permits, ct);
        }
        catch (NatsKVCreateException)
        {
            // Race condition on create, retry
            return await TryAcquireAsync(key, permits, ct);
        }
    }

    public async ValueTask<RateLimitResult> WaitAsync(
        string key,
        int permits = 1,
        TimeSpan timeout = default,
        CancellationToken ct = default)
    {
        if (timeout == default)
            timeout = TimeSpan.FromSeconds(30);

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = await TryAcquireAsync(key, permits, ct);
            if (result.IsAcquired)
                return result;

            // Wait before retry
            var waitTime = result.RetryAfter ?? TimeSpan.FromMilliseconds(100);
            if (waitTime > timeout)
                waitTime = TimeSpan.FromMilliseconds(100);

            await Task.Delay(waitTime, ct);
        }

        return RateLimitResult.Rejected(RateLimitRejectionReason.Timeout);
    }

    public async ValueTask<RateLimitStatistics?> GetStatisticsAsync(string key, CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var windowKey = GetWindowKey(key, now);

        try
        {
            var entry = await _store!.GetEntryAsync<byte[]>(windowKey, cancellationToken: ct);
            if (entry.Value == null)
            {
                return new RateLimitStatistics
                {
                    CurrentCount = 0,
                    Limit = _options.DefaultPermitLimit,
                    ResetAfter = GetResetTime(now)
                };
            }

            var count = BitConverter.ToInt64(entry.Value);
            return new RateLimitStatistics
            {
                CurrentCount = count,
                Limit = _options.DefaultPermitLimit,
                ResetAfter = GetResetTime(now)
            };
        }
        catch
        {
            return null;
        }
    }

    private string GetWindowKey(string key, DateTimeOffset now)
    {
        var windowStart = now.Ticks / _options.DefaultWindow.Ticks;
        return $"{_options.KeyPrefix}{key}:{windowStart}";
    }

    private TimeSpan GetResetTime(DateTimeOffset now)
    {
        var windowTicks = _options.DefaultWindow.Ticks;
        var currentWindowStart = (now.Ticks / windowTicks) * windowTicks;
        var nextWindowStart = currentWindowStart + windowTicks;
        return TimeSpan.FromTicks(nextWindowStart - now.Ticks);
    }
}
