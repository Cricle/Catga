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

    public CircuitBreaker(int failureThreshold = 5, TimeSpan? resetTimeout = null)
    {
        _failureThreshold = failureThreshold;
        _resetTimeoutTicks = (resetTimeout ?? TimeSpan.FromSeconds(30)).Ticks;
    }

    public CircuitState State
    {
        get
        {
            var currentState = (CircuitState)Interlocked.CompareExchange(ref _state, _state, _state);

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

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        // 快速路径 - 避免复杂的状态检查
        if (State == CircuitState.Open)
            throw new CircuitBreakerOpenException("Circuit breaker is open");

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
        var currentState = (CircuitState)Interlocked.CompareExchange(ref _state, _state, _state);

        if (currentState == CircuitState.HalfOpen)
        {
            // Transition back to Closed
            Interlocked.CompareExchange(ref _state, (int)CircuitState.Closed, (int)CircuitState.HalfOpen);
        }

        Interlocked.Exchange(ref _failureCount, 0);
    }

    private void OnFailure()
    {
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
