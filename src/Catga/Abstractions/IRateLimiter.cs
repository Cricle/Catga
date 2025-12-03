namespace Catga.Abstractions;

/// <summary>
/// Distributed rate limiter abstraction.
/// Provides cross-node rate limiting capabilities.
/// </summary>
public interface IDistributedRateLimiter
{
    /// <summary>Try to acquire permits without waiting.</summary>
    /// <param name="key">Rate limit key (e.g., user ID, API endpoint)</param>
    /// <param name="permits">Number of permits to acquire</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result indicating success or rejection with metadata</returns>
    ValueTask<RateLimitResult> TryAcquireAsync(
        string key,
        int permits = 1,
        CancellationToken ct = default);

    /// <summary>Wait to acquire permits with timeout.</summary>
    /// <param name="key">Rate limit key</param>
    /// <param name="permits">Number of permits to acquire</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result indicating success or timeout</returns>
    ValueTask<RateLimitResult> WaitAsync(
        string key,
        int permits = 1,
        TimeSpan timeout = default,
        CancellationToken ct = default);

    /// <summary>Get current rate limit statistics for a key.</summary>
    ValueTask<RateLimitStatistics?> GetStatisticsAsync(string key, CancellationToken ct = default);
}

/// <summary>Rate limit result.</summary>
public readonly record struct RateLimitResult
{
    /// <summary>Whether permits were acquired.</summary>
    public bool IsAcquired { get; init; }

    /// <summary>Reason for rejection if not acquired.</summary>
    public RateLimitRejectionReason Reason { get; init; }

    /// <summary>Suggested retry delay if rejected.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Number of remaining permits in current window.</summary>
    public long? RemainingPermits { get; init; }

    public static RateLimitResult Acquired(long? remaining = null) => new()
    {
        IsAcquired = true,
        Reason = RateLimitRejectionReason.None,
        RemainingPermits = remaining
    };

    public static RateLimitResult Rejected(RateLimitRejectionReason reason, TimeSpan? retryAfter = null) => new()
    {
        IsAcquired = false,
        Reason = reason,
        RetryAfter = retryAfter
    };
}

/// <summary>Rejection reasons.</summary>
public enum RateLimitRejectionReason
{
    None = 0,
    RateLimitExceeded = 1,
    Timeout = 2,
    Cancelled = 3
}

/// <summary>Rate limit statistics.</summary>
public readonly record struct RateLimitStatistics
{
    /// <summary>Current permit count used.</summary>
    public long CurrentCount { get; init; }

    /// <summary>Maximum permits allowed.</summary>
    public long Limit { get; init; }

    /// <summary>Time until window resets.</summary>
    public TimeSpan? ResetAfter { get; init; }
}

/// <summary>Rate limiter options.</summary>
public sealed class DistributedRateLimiterOptions
{
    /// <summary>Default permit limit per window.</summary>
    public int DefaultPermitLimit { get; set; } = 100;

    /// <summary>Default window duration.</summary>
    public TimeSpan DefaultWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Key prefix for storage.</summary>
    public string KeyPrefix { get; set; } = "catga:ratelimit:";

    /// <summary>Algorithm to use.</summary>
    public RateLimitAlgorithm Algorithm { get; set; } = RateLimitAlgorithm.SlidingWindow;

    /// <summary>Number of segments for sliding window.</summary>
    public int SlidingWindowSegments { get; set; } = 10;
}

/// <summary>Rate limiting algorithms.</summary>
public enum RateLimitAlgorithm
{
    /// <summary>Fixed window counter.</summary>
    FixedWindow,

    /// <summary>Sliding window with segments.</summary>
    SlidingWindow,

    /// <summary>Token bucket.</summary>
    TokenBucket
}
