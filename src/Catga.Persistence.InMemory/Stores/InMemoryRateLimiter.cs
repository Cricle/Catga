using System.Collections.Concurrent;
using Catga.Abstractions;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>
/// In-memory rate limiter for development and testing.
/// Thread-safe sliding window implementation.
/// </summary>
public sealed class InMemoryRateLimiter : IDistributedRateLimiter
{
    private readonly ConcurrentDictionary<string, RateLimitBucket> _buckets = new();
    private readonly int _defaultLimit;
    private readonly TimeSpan _defaultWindow;

    public InMemoryRateLimiter(int defaultLimit = 100, TimeSpan? defaultWindow = null)
    {
        _defaultLimit = defaultLimit;
        _defaultWindow = defaultWindow ?? TimeSpan.FromMinutes(1);
    }

    public ValueTask<RateLimitResult> TryAcquireAsync(string key, int permits = 1, CancellationToken ct = default)
    {
        var bucket = _buckets.GetOrAdd(key, _ => new RateLimitBucket(_defaultLimit, _defaultWindow));
        return ValueTask.FromResult(bucket.TryAcquire(permits));
    }

    public async ValueTask<RateLimitResult> WaitAsync(string key, int permits = 1, TimeSpan timeout = default, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout == default ? TimeSpan.FromSeconds(30) : timeout);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var result = await TryAcquireAsync(key, permits, ct);
            if (result.IsAcquired) return result;
            await Task.Delay(Math.Min(50, (int)(result.RetryAfter?.TotalMilliseconds ?? 50)), ct);
        }
        return ct.IsCancellationRequested
            ? RateLimitResult.Rejected(RateLimitRejectionReason.Cancelled)
            : RateLimitResult.Rejected(RateLimitRejectionReason.Timeout);
    }

    public ValueTask<RateLimitStatistics?> GetStatisticsAsync(string key, CancellationToken ct = default)
    {
        if (_buckets.TryGetValue(key, out var bucket))
        {
            return ValueTask.FromResult<RateLimitStatistics?>(bucket.GetStatistics());
        }
        return ValueTask.FromResult<RateLimitStatistics?>(null);
    }

    private sealed class RateLimitBucket
    {
        private readonly int _limit;
        private readonly TimeSpan _window;
        private readonly object _lock = new();
        private long _count;
        private DateTime _windowStart;

        public RateLimitBucket(int limit, TimeSpan window)
        {
            _limit = limit;
            _window = window;
            _windowStart = DateTime.UtcNow;
        }

        public RateLimitResult TryAcquire(int permits)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (now - _windowStart >= _window)
                {
                    _count = 0;
                    _windowStart = now;
                }

                if (_count + permits <= _limit)
                {
                    _count += permits;
                    return RateLimitResult.Acquired(_limit - _count);
                }

                var retryAfter = _windowStart + _window - now;
                return RateLimitResult.Rejected(RateLimitRejectionReason.RateLimitExceeded, retryAfter);
            }
        }

        public RateLimitStatistics GetStatistics()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (now - _windowStart >= _window)
                {
                    _count = 0;
                    _windowStart = now;
                }
                return new RateLimitStatistics
                {
                    CurrentCount = _count,
                    Limit = _limit,
                    ResetAfter = _windowStart + _window - now
                };
            }
        }
    }
}
