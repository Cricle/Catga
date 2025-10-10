namespace Catga.Configuration;

/// <summary>
/// Backoff strategy for retry operations
/// </summary>
public enum BackoffStrategy
{
    /// <summary>
    /// Fixed delay between retries
    /// </summary>
    Fixed = 0,

    /// <summary>
    /// Exponential backoff (delay doubles each time)
    /// </summary>
    Exponential = 1,

    /// <summary>
    /// Linear backoff (delay increases linearly)
    /// </summary>
    Linear = 2
}

/// <summary>
/// Retry behavior configuration options
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// Maximum number of retry attempts (default: 3)
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Backoff strategy (default: Exponential)
    /// </summary>
    public BackoffStrategy Strategy { get; set; } = BackoffStrategy.Exponential;

    /// <summary>
    /// Initial delay before first retry (default: 100ms)
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Maximum delay between retries (default: 5s)
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Exceptions that should trigger a retry (empty = all exceptions)
    /// </summary>
    public HashSet<Type> RetryableExceptions { get; set; } = new();

    /// <summary>
    /// Whether to retry on timeout exceptions (default: true)
    /// </summary>
    public bool RetryOnTimeout { get; set; } = true;

    /// <summary>
    /// Calculate delay for a given attempt
    /// </summary>
    public TimeSpan CalculateDelay(int attempt)
    {
        var delay = Strategy switch
        {
            BackoffStrategy.Fixed => InitialDelay,
            BackoffStrategy.Exponential => TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1)),
            BackoffStrategy.Linear => TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * attempt),
            _ => InitialDelay
        };

        return delay > MaxDelay ? MaxDelay : delay;
    }
}

/// <summary>
/// Timeout behavior configuration options
/// </summary>
public class TimeoutOptions
{
    /// <summary>
    /// Enable timeout control (default: false)
    /// </summary>
    public bool EnableTimeout { get; set; } = false;

    /// <summary>
    /// Default timeout for all requests (default: 30s)
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for query operations (default: 10s)
    /// </summary>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for command operations (default: 30s)
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Caching behavior configuration options
/// </summary>
public class CachingOptions
{
    /// <summary>
    /// Enable caching (default: false)
    /// </summary>
    public bool EnableCaching { get; set; } = false;

    /// <summary>
    /// Default cache expiration time (default: 5 minutes)
    /// </summary>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of cached items (default: 1000)
    /// </summary>
    public int MaxCachedItems { get; set; } = 1000;

    /// <summary>
    /// Cache sliding expiration (default: true)
    /// </summary>
    public bool UseSlidingExpiration { get; set; } = true;
}

/// <summary>
/// Circuit breaker configuration options
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Number of failures before opening circuit (default: 5)
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration to wait before attempting to close circuit (default: 30s)
    /// </summary>
    public TimeSpan ResetTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Duration for sampling failures (default: 60s)
    /// </summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Minimum throughput before circuit breaker activates (default: 10)
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Failure percentage threshold (0-100) (default: 50%)
    /// </summary>
    public int FailurePercentageThreshold { get; set; } = 50;
}

/// <summary>
/// Rate limiting configuration options
/// </summary>
public class RateLimitingOptions
{
    /// <summary>
    /// Requests allowed per second (default: 1000)
    /// </summary>
    public int RequestsPerSecond { get; set; } = 1000;

    /// <summary>
    /// Burst capacity for temporary spikes (default: 100)
    /// </summary>
    public int BurstCapacity { get; set; } = 100;

    /// <summary>
    /// Queue limit for pending requests (default: 0 = no queue)
    /// </summary>
    public int QueueLimit { get; set; } = 0;
}

/// <summary>
/// Batch operation configuration options
/// </summary>
public class BatchOptions
{
    /// <summary>
    /// Maximum batch size (default: 100)
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum degree of parallelism for batch operations (default: -1 = unlimited)
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;

    /// <summary>
    /// Whether to stop on first failure (default: false)
    /// </summary>
    public bool StopOnFirstFailure { get; set; } = false;

    /// <summary>
    /// Timeout for entire batch operation (default: 5 minutes)
    /// </summary>
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

