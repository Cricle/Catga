using Catga.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Catga.Persistence.Redis.RateLimiting;

/// <summary>
/// Redis-backed distributed rate limiter using Polly.
/// Provides cross-node rate limiting with sliding window algorithm.
/// </summary>
public sealed partial class RedisRateLimiter : IDistributedRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly DistributedRateLimiterOptions _options;
    private readonly ILogger<RedisRateLimiter> _logger;

    // Lua script for atomic sliding window rate limiting
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


    public RedisRateLimiter(
        IConnectionMultiplexer redis,
        IOptions<DistributedRateLimiterOptions> options,
        ILogger<RedisRateLimiter> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<RateLimitResult> TryAcquireAsync(
        string key,
        int permits = 1,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var fullKey = _options.KeyPrefix + key;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = (long)_options.DefaultWindow.TotalMilliseconds;

        try
        {
            RedisResult result;

            if (_options.Algorithm == RateLimitAlgorithm.SlidingWindow)
            {
                result = await db.ScriptEvaluateAsync(
                    SlidingWindowScript,
                    new RedisKey[] { fullKey },
                    new RedisValue[] { now, windowMs, _options.DefaultPermitLimit, permits, _options.SlidingWindowSegments });
            }
            else
            {
                result = await db.ScriptEvaluateAsync(
                    FixedWindowScript,
                    new RedisKey[] { fullKey },
                    new RedisValue[] { windowMs, _options.DefaultPermitLimit, permits });
            }

            var values = (RedisResult[])result!;
            var acquired = (int)values[0] == 1;
            var remaining = (long)values[1];
            var retryAfterMs = (long)values[2];

            if (acquired)
            {
                LogRateLimitAcquired(_logger, key, permits, remaining);
                return RateLimitResult.Acquired(remaining);
            }
            else
            {
                var retryAfter = TimeSpan.FromMilliseconds(retryAfterMs);
                LogRateLimitRejected(_logger, key, permits, retryAfter.TotalSeconds);
                return RateLimitResult.Rejected(RateLimitRejectionReason.RateLimitExceeded, retryAfter);
            }
        }
        catch (Exception ex)
        {
            LogRateLimitError(_logger, key, ex.Message);
            // Fail open - allow request on error
            return RateLimitResult.Acquired();
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

        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var result = await TryAcquireAsync(key, permits, ct);
            if (result.IsAcquired)
                return result;

            var delay = result.RetryAfter ?? TimeSpan.FromMilliseconds(100);
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (delay > remaining)
                delay = remaining;

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
        }

        return RateLimitResult.Rejected(RateLimitRejectionReason.Timeout);
    }

    public async ValueTask<RateLimitStatistics?> GetStatisticsAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var fullKey = _options.KeyPrefix + key;

        try
        {
            if (_options.Algorithm == RateLimitAlgorithm.SlidingWindow)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var windowStart = now - (long)_options.DefaultWindow.TotalMilliseconds;

                var count = await db.SortedSetLengthAsync(fullKey, windowStart, now);
                var ttl = await db.KeyTimeToLiveAsync(fullKey);

                return new RateLimitStatistics
                {
                    CurrentCount = count,
                    Limit = _options.DefaultPermitLimit,
                    ResetAfter = ttl
                };
            }
            else
            {
                var countStr = await db.StringGetAsync(fullKey);
                var count = countStr.HasValue ? (long)countStr : 0;
                var ttl = await db.KeyTimeToLiveAsync(fullKey);

                return new RateLimitStatistics
                {
                    CurrentCount = count,
                    Limit = _options.DefaultPermitLimit,
                    ResetAfter = ttl
                };
            }
        }
        catch
        {
            return null;
        }
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
