using System.Collections.Concurrent;
using Catga.Abstractions;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>In-memory rate limiter with sliding window.</summary>
public sealed class InMemoryRateLimiter(int defaultLimit = 100, TimeSpan? defaultWindow = null) : IDistributedRateLimiter
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly TimeSpan _window = defaultWindow ?? TimeSpan.FromMinutes(1);

    public ValueTask<RateLimitResult> TryAcquireAsync(string key, int permits = 1, CancellationToken ct = default)
        => ValueTask.FromResult(_buckets.GetOrAdd(key, _ => new Bucket(defaultLimit, _window)).TryAcquire(permits));

    public async ValueTask<RateLimitResult> WaitAsync(string key, int permits = 1, TimeSpan timeout = default, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout == default ? TimeSpan.FromSeconds(30) : timeout);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var r = await TryAcquireAsync(key, permits, ct);
            if (r.IsAcquired) return r;
            await Task.Delay(Math.Min(50, (int)(r.RetryAfter?.TotalMilliseconds ?? 50)), ct);
        }
        return RateLimitResult.Rejected(ct.IsCancellationRequested ? RateLimitRejectionReason.Cancelled : RateLimitRejectionReason.Timeout);
    }

    public ValueTask<RateLimitStatistics?> GetStatisticsAsync(string key, CancellationToken ct = default)
        => ValueTask.FromResult(_buckets.TryGetValue(key, out var b) ? b.GetStats() : null);

    private sealed class Bucket(int limit, TimeSpan window)
    {
        private readonly Lock _lock = new();
        private long _count;
        private DateTime _start = DateTime.UtcNow;

        public RateLimitResult TryAcquire(int permits)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (now - _start >= window) { _count = 0; _start = now; }
                if (_count + permits <= limit) { _count += permits; return RateLimitResult.Acquired(limit - _count); }
                return RateLimitResult.Rejected(RateLimitRejectionReason.RateLimitExceeded, _start + window - now);
            }
        }

        public RateLimitStatistics? GetStats()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (now - _start >= window) { _count = 0; _start = now; }
                return new() { CurrentCount = _count, Limit = limit, ResetAfter = _start + window - now };
            }
        }
    }
}
