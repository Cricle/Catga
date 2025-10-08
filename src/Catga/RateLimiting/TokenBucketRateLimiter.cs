namespace Catga.RateLimiting;

/// <summary>
/// Lock-free token bucket rate limiter using atomic operations (non-blocking, AOT-compatible)
/// </summary>
public sealed class TokenBucketRateLimiter
{
    private readonly int _capacity;
    private readonly int _refillRate;

    private long _tokens;
    private long _lastRefillTicks;

    public TokenBucketRateLimiter(int capacity, int refillRatePerSecond)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (refillRatePerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(refillRatePerSecond));

        _capacity = capacity;
        _refillRate = refillRatePerSecond;
        _tokens = capacity;
        _lastRefillTicks = DateTime.UtcNow.Ticks;
    }

    public long AvailableTokens
    {
        get
        {
            RefillTokens();
            return Interlocked.Read(ref _tokens);
        }
    }

    public bool TryAcquire(int tokens = 1)
    {
        RefillTokens();

        // Optimized lock-free atomic decrement
        while (true)
        {
            var current = Interlocked.Read(ref _tokens);

            if (current < tokens)
                return false;

            if (Interlocked.CompareExchange(ref _tokens, current - tokens, current) == current)
                return true;
        }
    }

    public async Task<bool> WaitForTokenAsync(
        int tokens = 1,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var maxWait = timeout ?? TimeSpan.FromSeconds(10);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

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
        var now = DateTime.UtcNow.Ticks;
        var lastRefill = Interlocked.Read(ref _lastRefillTicks);
        var elapsed = TimeSpan.FromTicks(now - lastRefill);

        if (elapsed.TotalSeconds < 1.0)
            return;

        var tokensToAdd = (long)(elapsed.TotalSeconds * _refillRate);
        if (tokensToAdd <= 0)
            return;

        // Lock-free atomic update
        while (true)
        {
            var currentTokens = Interlocked.Read(ref _tokens);
            var newTokens = Math.Min(_capacity, currentTokens + tokensToAdd);

            if (Interlocked.CompareExchange(ref _tokens, newTokens, currentTokens) == currentTokens)
            {
                Interlocked.Exchange(ref _lastRefillTicks, now);
                break;
            }
        }
    }
}

public class RateLimitExceededException(string message) : Exception(message);
