using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Catga.Abstractions;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>In-memory rate limiter using System.Threading.RateLimiting.</summary>
public sealed class InMemoryRateLimiter : IDistributedRateLimiter, IDisposable
{
    private readonly ConcurrentDictionary<string, SlidingWindowRateLimiter> _limiters = new();
    private readonly int _defaultLimit;
    private readonly TimeSpan _window;

    public InMemoryRateLimiter(int defaultLimit = 100, TimeSpan? defaultWindow = null)
    {
        _defaultLimit = defaultLimit;
        _window = defaultWindow ?? TimeSpan.FromMinutes(1);
    }

    private SlidingWindowRateLimiter GetOrCreateLimiter(string key)
    {
        return _limiters.GetOrAdd(key, _ => new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = _defaultLimit,
            Window = _window,
            SegmentsPerWindow = 4,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        }));
    }

    public async ValueTask<RateLimitResult> TryAcquireAsync(string key, int permits = 1, CancellationToken ct = default)
    {
        var limiter = GetOrCreateLimiter(key);
        using var lease = await limiter.AcquireAsync(permits, ct);
        if (lease.IsAcquired)
        {
            return RateLimitResult.Acquired(_defaultLimit);
        }

        lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
        return RateLimitResult.Rejected(RateLimitRejectionReason.RateLimitExceeded, retryAfter);
    }

    public async ValueTask<RateLimitResult> WaitAsync(string key, int permits = 1, TimeSpan timeout = default, CancellationToken ct = default)
    {
        var actualTimeout = timeout == default ? TimeSpan.FromSeconds(30) : timeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(actualTimeout);

        try
        {
            var limiter = GetOrCreateLimiter(key);
            using var lease = await limiter.AcquireAsync(permits, cts.Token);
            if (lease.IsAcquired)
            {
                return RateLimitResult.Acquired(_defaultLimit);
            }

            lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
            return RateLimitResult.Rejected(RateLimitRejectionReason.RateLimitExceeded, retryAfter);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return RateLimitResult.Rejected(RateLimitRejectionReason.Cancelled);
        }
        catch (OperationCanceledException)
        {
            return RateLimitResult.Rejected(RateLimitRejectionReason.Timeout);
        }
    }

    public ValueTask<RateLimitStatistics?> GetStatisticsAsync(string key, CancellationToken ct = default)
    {
        if (_limiters.TryGetValue(key, out var limiter))
        {
            var stats = limiter.GetStatistics();
            if (stats != null)
                return ValueTask.FromResult<RateLimitStatistics?>(new RateLimitStatistics
                {
                    CurrentCount = stats.CurrentAvailablePermits,
                    Limit = _defaultLimit,
                    ResetAfter = _window
                });
        }
        return ValueTask.FromResult<RateLimitStatistics?>(null);
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
        _limiters.Clear();
    }
}
