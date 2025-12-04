using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.RateLimiting;
using Catga.Abstractions;
using Catga.Observability;

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
        using var activity = CatgaActivitySource.Source.StartActivity("RateLimit.TryAcquire", ActivityKind.Internal);
        activity?.SetTag(CatgaActivitySource.Tags.RateLimitKey, key);
        activity?.SetTag(CatgaActivitySource.Tags.RateLimitPermits, permits);

        var limiter = GetOrCreateLimiter(key);
        using var lease = await limiter.AcquireAsync(permits, ct);

        if (lease.IsAcquired)
        {
            CatgaDiagnostics.RateLimitAcquired.Add(1);
            activity?.AddActivityEvent(CatgaActivitySource.Events.RateLimitAcquired);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return RateLimitResult.Acquired(_defaultLimit);
        }

        CatgaDiagnostics.RateLimitRejected.Add(1);
        activity?.AddActivityEvent(CatgaActivitySource.Events.RateLimitRejected);

        lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
        return RateLimitResult.Rejected(RateLimitRejectionReason.RateLimitExceeded, retryAfter);
    }

    public async ValueTask<RateLimitResult> WaitAsync(string key, int permits = 1, TimeSpan timeout = default, CancellationToken ct = default)
    {
        using var activity = CatgaActivitySource.Source.StartActivity("RateLimit.Wait", ActivityKind.Internal);
        activity?.SetTag(CatgaActivitySource.Tags.RateLimitKey, key);
        activity?.SetTag(CatgaActivitySource.Tags.RateLimitPermits, permits);

        var actualTimeout = timeout == default ? TimeSpan.FromSeconds(30) : timeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(actualTimeout);

        var sw = Stopwatch.StartNew();

        try
        {
            var limiter = GetOrCreateLimiter(key);
            using var lease = await limiter.AcquireAsync(permits, cts.Token);

            sw.Stop();
            CatgaDiagnostics.RateLimitWaitDuration.Record(sw.Elapsed.TotalMilliseconds);

            if (lease.IsAcquired)
            {
                CatgaDiagnostics.RateLimitAcquired.Add(1);
                activity?.AddActivityEvent(CatgaActivitySource.Events.RateLimitAcquired);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return RateLimitResult.Acquired(_defaultLimit);
            }

            CatgaDiagnostics.RateLimitRejected.Add(1);
            activity?.AddActivityEvent(CatgaActivitySource.Events.RateLimitRejected);

            lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);
            return RateLimitResult.Rejected(RateLimitRejectionReason.RateLimitExceeded, retryAfter);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            CatgaDiagnostics.RateLimitRejected.Add(1);
            return RateLimitResult.Rejected(RateLimitRejectionReason.Cancelled);
        }
        catch (OperationCanceledException)
        {
            CatgaDiagnostics.RateLimitRejected.Add(1);
            activity?.AddActivityEvent(CatgaActivitySource.Events.RateLimitTimeout);
            return RateLimitResult.Rejected(RateLimitRejectionReason.Timeout);
        }
    }

    public ValueTask<RateLimitStatistics?> GetStatisticsAsync(string key, CancellationToken ct = default)
    {
        if (_limiters.TryGetValue(key, out var limiter))
        {
            var stats = limiter.GetStatistics();
            if (stats != null)
            {
                return ValueTask.FromResult<RateLimitStatistics?>(new RateLimitStatistics
                {
                    CurrentCount = stats.CurrentAvailablePermits,
                    Limit = _defaultLimit,
                    ResetAfter = _window
                });
            }
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
