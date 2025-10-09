namespace Catga.Resilience;

/// <summary>
/// Lock-free circuit breaker using atomic operations (non-blocking, AOT-compatible)
/// </summary>
public sealed class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly long _resetTimeoutTicks;

    private int _state = (int)CircuitState.Closed;
    private long _failureCount;
    private long _lastFailureTimeTicks;
    private long _openedAtTicks;

    // Observability: Track metrics
    private long _totalCalls;
    private long _successfulCalls;
    private long _failedCalls;
    private long _rejectedCalls;

    public CircuitBreaker(int failureThreshold = 5, TimeSpan? resetTimeout = null)
    {
        _failureThreshold = failureThreshold;
        _resetTimeoutTicks = (resetTimeout ?? TimeSpan.FromSeconds(30)).Ticks;
    }

    public CircuitState State
    {
        get
        {
            // P1 Optimization: Use Volatile.Read instead of CAS for read-only operation
            var currentState = (CircuitState)Volatile.Read(ref _state);

            if (currentState == CircuitState.Open)
            {
                var openedAt = Interlocked.Read(ref _openedAtTicks);
                if (DateTime.UtcNow.Ticks - openedAt >= _resetTimeoutTicks)
                {
                    // Try to transition to HalfOpen
                    Interlocked.CompareExchange(ref _state, (int)CircuitState.HalfOpen, (int)CircuitState.Open);
                    Interlocked.Exchange(ref _failureCount, 0);
                    return CircuitState.HalfOpen;
                }
            }

            return currentState;
        }
    }

    public long FailureCount => Interlocked.Read(ref _failureCount);

    // Observability: Monitoring properties
    /// <summary>
    /// Total number of calls attempted
    /// </summary>
    public long TotalCalls => Interlocked.Read(ref _totalCalls);

    /// <summary>
    /// Number of successful calls
    /// </summary>
    public long SuccessfulCalls => Interlocked.Read(ref _successfulCalls);

    /// <summary>
    /// Number of failed calls
    /// </summary>
    public long FailedCalls => Interlocked.Read(ref _failedCalls);

    /// <summary>
    /// Number of rejected calls (circuit open)
    /// </summary>
    public long RejectedCalls => Interlocked.Read(ref _rejectedCalls);

    /// <summary>
    /// Success rate (0.0 to 1.0)
    /// </summary>
    public double SuccessRate
    {
        get
        {
            var total = TotalCalls - RejectedCalls; // Exclude rejected
            return total > 0 ? SuccessfulCalls / (double)total : 0.0;
        }
    }

    /// <summary>
    /// Rejection rate (0.0 to 1.0)
    /// </summary>
    public double RejectionRate
    {
        get
        {
            var total = TotalCalls;
            return total > 0 ? RejectedCalls / (double)total : 0.0;
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        // Observability: Track total calls
        Interlocked.Increment(ref _totalCalls);

        // Fast path - avoid complex state checks
        if (State == CircuitState.Open)
        {
            // Observability: Track rejections
            Interlocked.Increment(ref _rejectedCalls);
            throw new CircuitBreakerOpenException("Circuit breaker is open");
        }

        try
        {
            var result = await action();
            OnSuccess();
            return result;
        }
        catch (Exception)
        {
            OnFailure();
            throw;
        }
    }

    private void OnSuccess()
    {
        // Observability: Track success
        Interlocked.Increment(ref _successfulCalls);

        // P1 Optimization: Use Volatile.Read instead of CAS for read-only operation
        var currentState = (CircuitState)Volatile.Read(ref _state);

        if (currentState == CircuitState.HalfOpen)
        {
            // Transition back to Closed
            Interlocked.CompareExchange(ref _state, (int)CircuitState.Closed, (int)CircuitState.HalfOpen);
        }

        Interlocked.Exchange(ref _failureCount, 0);
    }

    private void OnFailure()
    {
        // Observability: Track failure
        Interlocked.Increment(ref _failedCalls);

        var newCount = Interlocked.Increment(ref _failureCount);
        Interlocked.Exchange(ref _lastFailureTimeTicks, DateTime.UtcNow.Ticks);

        if (newCount >= _failureThreshold)
        {
            // Open the circuit
            Interlocked.CompareExchange(ref _state, (int)CircuitState.Open, (int)CircuitState.Closed);
            Interlocked.CompareExchange(ref _state, (int)CircuitState.Open, (int)CircuitState.HalfOpen);
            Interlocked.Exchange(ref _openedAtTicks, DateTime.UtcNow.Ticks);
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _state, (int)CircuitState.Closed);
        Interlocked.Exchange(ref _failureCount, 0);
    }
}

public enum CircuitState
{
    Closed = 0,
    Open = 1,
    HalfOpen = 2
}

public class CircuitBreakerOpenException(string message) : Exception(message);
