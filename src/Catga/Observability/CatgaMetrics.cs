using System.Diagnostics;

namespace Catga.Observability;

/// <summary>
/// Catga performance metrics for monitoring and diagnostics
/// Thread-safe, lock-free implementation using Interlocked operations
/// </summary>
public sealed class CatgaMetrics
{
    // Request metrics
    private long _totalRequests;
    private long _successfulRequests;
    private long _failedRequests;
    private long _totalRequestDurationTicks;

    // Event metrics
    private long _totalEvents;
    private long _totalEventHandlers;

    // Batch metrics
    private long _totalBatchRequests;
    private long _totalBatchEvents;

    // Resilience metrics
    private long _rateLimitedRequests;
    private long _concurrencyLimitedRequests;
    private long _circuitBreakerOpenRequests;

    /// <summary>
    /// Total number of requests processed
    /// </summary>
    public long TotalRequests => Interlocked.Read(ref _totalRequests);

    /// <summary>
    /// Number of successful requests
    /// </summary>
    public long SuccessfulRequests => Interlocked.Read(ref _successfulRequests);

    /// <summary>
    /// Number of failed requests
    /// </summary>
    public long FailedRequests => Interlocked.Read(ref _failedRequests);

    /// <summary>
    /// Success rate (0.0 to 1.0)
    /// </summary>
    public double SuccessRate
    {
        get
        {
            var total = TotalRequests;
            return total > 0 ? SuccessfulRequests / (double)total : 0.0;
        }
    }

    /// <summary>
    /// Average request duration in milliseconds
    /// </summary>
    public double AverageRequestDurationMs
    {
        get
        {
            var total = TotalRequests;
            if (total == 0) return 0.0;
            var totalTicks = Interlocked.Read(ref _totalRequestDurationTicks);
            return TimeSpan.FromTicks(totalTicks / total).TotalMilliseconds;
        }
    }

    /// <summary>
    /// Total number of events published
    /// </summary>
    public long TotalEvents => Interlocked.Read(ref _totalEvents);

    /// <summary>
    /// Total number of event handlers executed
    /// </summary>
    public long TotalEventHandlers => Interlocked.Read(ref _totalEventHandlers);

    /// <summary>
    /// Average event handlers per event
    /// </summary>
    public double AverageHandlersPerEvent
    {
        get
        {
            var events = TotalEvents;
            return events > 0 ? TotalEventHandlers / (double)events : 0.0;
        }
    }

    /// <summary>
    /// Total batch requests processed
    /// </summary>
    public long TotalBatchRequests => Interlocked.Read(ref _totalBatchRequests);

    /// <summary>
    /// Total batch events published
    /// </summary>
    public long TotalBatchEvents => Interlocked.Read(ref _totalBatchEvents);

    /// <summary>
    /// Requests rejected by rate limiter
    /// </summary>
    public long RateLimitedRequests => Interlocked.Read(ref _rateLimitedRequests);

    /// <summary>
    /// Requests rejected by concurrency limiter
    /// </summary>
    public long ConcurrencyLimitedRequests => Interlocked.Read(ref _concurrencyLimitedRequests);

    /// <summary>
    /// Requests rejected by circuit breaker
    /// </summary>
    public long CircuitBreakerOpenRequests => Interlocked.Read(ref _circuitBreakerOpenRequests);

    /// <summary>
    /// Total resilience rejections
    /// </summary>
    public long TotalResilienceRejections =>
        RateLimitedRequests + ConcurrencyLimitedRequests + CircuitBreakerOpenRequests;

    /// <summary>
    /// Resilience rejection rate (0.0 to 1.0)
    /// </summary>
    public double ResilienceRejectionRate
    {
        get
        {
            var total = TotalRequests;
            return total > 0 ? TotalResilienceRejections / (double)total : 0.0;
        }
    }

    // Internal tracking methods
    internal void RecordRequest(bool success, TimeSpan duration)
    {
        Interlocked.Increment(ref _totalRequests);
        if (success)
            Interlocked.Increment(ref _successfulRequests);
        else
            Interlocked.Increment(ref _failedRequests);

        Interlocked.Add(ref _totalRequestDurationTicks, duration.Ticks);
    }

    internal void RecordEvent(int handlerCount)
    {
        Interlocked.Increment(ref _totalEvents);
        Interlocked.Add(ref _totalEventHandlers, handlerCount);
    }

    internal void RecordBatchRequest(int count)
    {
        Interlocked.Add(ref _totalBatchRequests, count);
    }

    internal void RecordBatchEvent(int count)
    {
        Interlocked.Add(ref _totalBatchEvents, count);
    }

    internal void RecordRateLimited()
    {
        Interlocked.Increment(ref _rateLimitedRequests);
    }

    internal void RecordConcurrencyLimited()
    {
        Interlocked.Increment(ref _concurrencyLimitedRequests);
    }

    internal void RecordCircuitBreakerOpen()
    {
        Interlocked.Increment(ref _circuitBreakerOpenRequests);
    }

    /// <summary>
    /// Reset all metrics (for testing or periodic reset)
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _totalRequests, 0);
        Interlocked.Exchange(ref _successfulRequests, 0);
        Interlocked.Exchange(ref _failedRequests, 0);
        Interlocked.Exchange(ref _totalRequestDurationTicks, 0);
        Interlocked.Exchange(ref _totalEvents, 0);
        Interlocked.Exchange(ref _totalEventHandlers, 0);
        Interlocked.Exchange(ref _totalBatchRequests, 0);
        Interlocked.Exchange(ref _totalBatchEvents, 0);
        Interlocked.Exchange(ref _rateLimitedRequests, 0);
        Interlocked.Exchange(ref _concurrencyLimitedRequests, 0);
        Interlocked.Exchange(ref _circuitBreakerOpenRequests, 0);
    }

    /// <summary>
    /// Get a snapshot of current metrics
    /// </summary>
    public CatgaMetricsSnapshot GetSnapshot()
    {
        return new CatgaMetricsSnapshot
        {
            TotalRequests = TotalRequests,
            SuccessfulRequests = SuccessfulRequests,
            FailedRequests = FailedRequests,
            SuccessRate = SuccessRate,
            AverageRequestDurationMs = AverageRequestDurationMs,
            TotalEvents = TotalEvents,
            TotalEventHandlers = TotalEventHandlers,
            AverageHandlersPerEvent = AverageHandlersPerEvent,
            TotalBatchRequests = TotalBatchRequests,
            TotalBatchEvents = TotalBatchEvents,
            RateLimitedRequests = RateLimitedRequests,
            ConcurrencyLimitedRequests = ConcurrencyLimitedRequests,
            CircuitBreakerOpenRequests = CircuitBreakerOpenRequests,
            TotalResilienceRejections = TotalResilienceRejections,
            ResilienceRejectionRate = ResilienceRejectionRate,
            Timestamp = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Immutable snapshot of Catga metrics at a point in time
/// </summary>
public sealed class CatgaMetricsSnapshot
{
    public long TotalRequests { get; init; }
    public long SuccessfulRequests { get; init; }
    public long FailedRequests { get; init; }
    public double SuccessRate { get; init; }
    public double AverageRequestDurationMs { get; init; }
    public long TotalEvents { get; init; }
    public long TotalEventHandlers { get; init; }
    public double AverageHandlersPerEvent { get; init; }
    public long TotalBatchRequests { get; init; }
    public long TotalBatchEvents { get; init; }
    public long RateLimitedRequests { get; init; }
    public long ConcurrencyLimitedRequests { get; init; }
    public long CircuitBreakerOpenRequests { get; init; }
    public long TotalResilienceRejections { get; init; }
    public double ResilienceRejectionRate { get; init; }
    public DateTime Timestamp { get; init; }
}
