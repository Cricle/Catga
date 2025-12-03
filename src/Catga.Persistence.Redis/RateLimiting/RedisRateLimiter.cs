using Catga.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.RateLimiting;

/// <summary>Redis-backed distributed rate limiter.</summary>
public sealed partial class RedisRateLimiter(IConnectionMultiplexer redis, IOptions<DistributedRateLimiterOptions> options, ILogger<RedisRateLimiter> logger) : IDistributedRateLimiter
{
    private readonly DistributedRateLimiterOptions _opts = options.Value;

    private static readonly string SlidingWindowScript = """
        local key = KEYS[1]
        local now = tonumber(ARGV[1])
        local window = tonumber(ARGV[2])
        local limit = tonumber(ARGV[3])
        local permits = tonumber(ARGV[4])
        local segments = tonumber(ARGV[5])

        local segment_duration = window / segments
        local current_segment = math.floor(now / segment_duration)
        local window_start = now - window

        -- Remove expired segments
        redis.call('ZREMRANGEBYSCORE', key, '-inf', window_start)

        -- Count current requests in window
        local current_count = redis.call('ZCARD', key)

        if current_count + permits <= limit then
            -- Add new requests
            for i = 1, permits do
                redis.call('ZADD', key, now, now .. ':' .. i .. ':' .. math.random(1000000))
            end
            redis.call('EXPIRE', key, math.ceil(window / 1000) + 1)
            return {1, limit - current_count - permits, 0}
        else
            -- Calculate retry after
            local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
            local retry_after = 0
            if #oldest >= 2 then
                retry_after = math.ceil((tonumber(oldest[2]) + window - now))
            end
            return {0, 0, retry_after}
        end
        """;

    private static readonly string FixedWindowScript = """
        local key = KEYS[1]
        local window = tonumber(ARGV[1])
        local limit = tonumber(ARGV[2])
        local permits = tonumber(ARGV[3])

        local current = redis.call('GET', key)
        current = current and tonumber(current) or 0

        if current + permits <= limit then
            local new_count = redis.call('INCRBY', key, permits)
            if new_count == permits then
                redis.call('PEXPIRE', key, window)
            end
            local ttl = redis.call('PTTL', key)
            return {1, limit - new_count, ttl}
        else
            local ttl = redis.call('PTTL', key)
            return {0, 0, ttl > 0 and ttl or window}
        end
        """;

    public async ValueTask<RateLimitResult> TryAcquireAsync(string key, int permits = 1, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var fullKey = _opts.KeyPrefix + key;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = (long)_opts.DefaultWindow.TotalMilliseconds;
        try
        {
            var result = _opts.Algorithm == RateLimitAlgorithm.SlidingWindow
                ? await db.ScriptEvaluateAsync(SlidingWindowScript, [fullKey], [now, windowMs, _opts.DefaultPermitLimit, permits, _opts.SlidingWindowSegments])
                : await db.ScriptEvaluateAsync(FixedWindowScript, [fullKey], [windowMs, _opts.DefaultPermitLimit, permits]);
            var v = (RedisResult[])result!;
            var acquired = (int)v[0] == 1;
            if (acquired) { LogRateLimitAcquired(logger, key, permits, (long)v[1]); return RateLimitResult.Acquired((long)v[1]); }
            var retryAfter = TimeSpan.FromMilliseconds((long)v[2]);
            LogRateLimitRejected(logger, key, permits, retryAfter.TotalSeconds);
            return RateLimitResult.Rejected(RateLimitRejectionReason.RateLimitExceeded, retryAfter);
        }
        catch (Exception ex) { LogRateLimitError(logger, key, ex.Message); return RateLimitResult.Acquired(); }
    }

    public async ValueTask<RateLimitResult> WaitAsync(string key, int permits = 1, TimeSpan timeout = default, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout == default ? TimeSpan.FromSeconds(30) : timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var r = await TryAcquireAsync(key, permits, ct);
            if (r.IsAcquired) return r;
            var delay = TimeSpan.FromMilliseconds(Math.Min((r.RetryAfter ?? TimeSpan.FromMilliseconds(100)).TotalMilliseconds, (deadline - DateTimeOffset.UtcNow).TotalMilliseconds));
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
        }
        return RateLimitResult.Rejected(RateLimitRejectionReason.Timeout);
    }

    public async ValueTask<RateLimitStatistics?> GetStatisticsAsync(string key, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var fullKey = _opts.KeyPrefix + key;
        try
        {
            long count; TimeSpan? ttl;
            if (_opts.Algorithm == RateLimitAlgorithm.SlidingWindow)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                count = await db.SortedSetLengthAsync(fullKey, now - (long)_opts.DefaultWindow.TotalMilliseconds, now);
            }
            else { var v = await db.StringGetAsync(fullKey); count = v.HasValue ? (long)v : 0; }
            ttl = await db.KeyTimeToLiveAsync(fullKey);
            return new() { CurrentCount = count, Limit = _opts.DefaultPermitLimit, ResetAfter = ttl };
        }
        catch { return null; }
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rate limit acquired: {Key} ({Permits} permits, {Remaining} remaining)")]
    private static partial void LogRateLimitAcquired(ILogger logger, string key, int permits, long remaining);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit rejected: {Key} ({Permits} permits, retry after {RetryAfterSeconds}s)")]
    private static partial void LogRateLimitRejected(ILogger logger, string key, int permits, double retryAfterSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rate limit error: {Key} - {Error}")]
    private static partial void LogRateLimitError(ILogger logger, string key, string error);

    #endregion
}
