using System.Collections.Concurrent;

namespace Catga.Flow.Dsl;

/// <summary>
/// Circuit breaker states.
/// </summary>
public enum CircuitBreakerState
{
    Closed,    // Normal operation
    Open,      // Circuit is open, failing fast
    HalfOpen   // Testing if the service has recovered
}

/// <summary>
/// Circuit breaker for ForEach operations.
/// </summary>
public class ForEachCircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _breakDuration;
    private readonly object _lock = new();

    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private DateTime _nextAttemptTime = DateTime.MinValue;

    public ForEachCircuitBreaker(int failureThreshold, TimeSpan breakDuration)
    {
        _failureThreshold = failureThreshold;
        _breakDuration = breakDuration;
    }

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public int FailureCount
    {
        get
        {
            lock (_lock)
            {
                return _failureCount;
            }
        }
    }

    /// <summary>
    /// Check if the circuit breaker allows execution.
    /// </summary>
    public bool CanExecute()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    return true;

                case CircuitBreakerState.Open:
                    if (DateTime.UtcNow >= _nextAttemptTime)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                        return true;
                    }
                    return false;

                case CircuitBreakerState.HalfOpen:
                    return true;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Record a successful execution.
    /// </summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failureCount = 0;
            _state = CircuitBreakerState.Closed;
        }
    }

    /// <summary>
    /// Record a failed execution.
    /// </summary>
    public void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;

            if (_state == CircuitBreakerState.HalfOpen)
            {
                // If we're in half-open and get a failure, go back to open
                _state = CircuitBreakerState.Open;
                _nextAttemptTime = DateTime.UtcNow.Add(_breakDuration);
            }
            else if (_failureCount >= _failureThreshold)
            {
                // If we've reached the failure threshold, open the circuit
                _state = CircuitBreakerState.Open;
                _nextAttemptTime = DateTime.UtcNow.Add(_breakDuration);
            }
        }
    }

    /// <summary>
    /// Reset the circuit breaker to closed state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _failureCount = 0;
            _lastFailureTime = DateTime.MinValue;
            _nextAttemptTime = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Get circuit breaker statistics.
    /// </summary>
    public CircuitBreakerStats GetStats()
    {
        lock (_lock)
        {
            return new CircuitBreakerStats
            {
                State = _state,
                FailureCount = _failureCount,
                FailureThreshold = _failureThreshold,
                LastFailureTime = _lastFailureTime,
                NextAttemptTime = _nextAttemptTime,
                BreakDuration = _breakDuration
            };
        }
    }
}

/// <summary>
/// Circuit breaker statistics.
/// </summary>
public class CircuitBreakerStats
{
    public CircuitBreakerState State { get; set; }
    public int FailureCount { get; set; }
    public int FailureThreshold { get; set; }
    public DateTime LastFailureTime { get; set; }
    public DateTime NextAttemptTime { get; set; }
    public TimeSpan BreakDuration { get; set; }
}

/// <summary>
/// Exception thrown when circuit breaker is open.
/// </summary>
public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerStats Stats { get; }

    public CircuitBreakerOpenException(CircuitBreakerStats stats)
        : base($"Circuit breaker is open. State: {stats.State}, Failures: {stats.FailureCount}/{stats.FailureThreshold}")
    {
        Stats = stats;
    }
}

/// <summary>
/// Circuit breaker registry for managing multiple circuit breakers.
/// </summary>
public static class CircuitBreakerRegistry
{
    private static readonly ConcurrentDictionary<string, ForEachCircuitBreaker> _circuitBreakers = new();

    /// <summary>
    /// Get or create a circuit breaker for a specific key.
    /// </summary>
    public static ForEachCircuitBreaker GetOrCreate(string key, int failureThreshold, TimeSpan breakDuration)
    {
        return _circuitBreakers.GetOrAdd(key, _ => new ForEachCircuitBreaker(failureThreshold, breakDuration));
    }

    /// <summary>
    /// Remove a circuit breaker.
    /// </summary>
    public static bool Remove(string key)
    {
        return _circuitBreakers.TryRemove(key, out _);
    }

    /// <summary>
    /// Get all circuit breaker keys.
    /// </summary>
    public static IEnumerable<string> GetKeys()
    {
        return _circuitBreakers.Keys;
    }

    /// <summary>
    /// Get statistics for all circuit breakers.
    /// </summary>
    public static Dictionary<string, CircuitBreakerStats> GetAllStats()
    {
        return _circuitBreakers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetStats()
        );
    }

    /// <summary>
    /// Reset all circuit breakers.
    /// </summary>
    public static void ResetAll()
    {
        foreach (var circuitBreaker in _circuitBreakers.Values)
        {
            circuitBreaker.Reset();
        }
    }
}
