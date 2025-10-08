using System.Diagnostics;

namespace Catga.RateLimiting;

/// <summary>
/// Lock-free token bucket rate limiter using atomic operations (non-blocking, AOT-compatible)
/// P0 Optimization: Use Stopwatch.GetTimestamp() instead of DateTime.UtcNow.Ticks for better performance
/// </summary>
public sealed class TokenBucketRateLimiter
{
    private readonly int _capacity;
    private readonly long _refillRatePerTick; // Refill rate scaled: (rate * SCALE / Stopwatch.Frequency)

    private long _tokens; // Scaled by SCALE for precision
    private long _lastRefillTicks;
    
    // Scale factor for sub-token precision (allows fractional tokens as integers)
    private const int SCALE = 1000;

    public TokenBucketRateLimiter(int capacity, int refillRatePerSecond)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (refillRatePerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(refillRatePerSecond));

        _capacity = capacity;
        // P0: Pre-calculate refill rate per tick (avoids division in hot path)
        // refillRatePerSecond tokens/sec * SCALE / Stopwatch.Frequency ticks/sec = scaled_tokens/tick
        _refillRatePerTick = (long)((double)refillRatePerSecond * SCALE / Stopwatch.Frequency);
        _tokens = capacity * SCALE;
        _lastRefillTicks = Stopwatch.GetTimestamp();
    }

    public long AvailableTokens
    {
        get
        {
            RefillTokens();
            return Interlocked.Read(ref _tokens) / SCALE;
        }
    }

    public bool TryAcquire(int tokens = 1)
    {
        RefillTokens();

        var scaledTokens = tokens * SCALE;
        
        // Optimized lock-free atomic decrement
        while (true)
        {
            var current = Interlocked.Read(ref _tokens);

            if (current < scaledTokens)
                return false;

            if (Interlocked.CompareExchange(ref _tokens, current - scaledTokens, current) == current)
                return true;
        }
    }

    public async Task<bool> WaitForTokenAsync(
        int tokens = 1,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var maxWait = timeout ?? TimeSpan.FromSeconds(10);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < maxWait)
        {
            if (TryAcquire(tokens))
                return true;

            // Non-blocking async delay
            await Task.Delay(10, cancellationToken);
        }

        return false;
    }

    private void RefillTokens()
    {
        var now = Stopwatch.GetTimestamp();
        var lastRefill = Interlocked.Read(ref _lastRefillTicks);
        var elapsedTicks = now - lastRefill;

        // P0: Minimum refill interval (avoid too frequent updates)
        // Only refill if enough time has passed to generate at least 1 token
        if (elapsedTicks * _refillRatePerTick < SCALE)
            return;

        // P0: Pure integer arithmetic (no floating point)
        var tokensToAdd = elapsedTicks * _refillRatePerTick;
        if (tokensToAdd <= 0)
            return;

        // Lock-free atomic update
        while (true)
        {
            var currentTokens = Interlocked.Read(ref _tokens);
            var capacityScaled = (long)_capacity * SCALE;
            var newTokens = Math.Min(capacityScaled, currentTokens + tokensToAdd);

            if (Interlocked.CompareExchange(ref _tokens, newTokens, currentTokens) == currentTokens)
            {
                Interlocked.Exchange(ref _lastRefillTicks, now);
                break;
            }
        }
    }
}

public class RateLimitExceededException(string message) : Exception(message);
