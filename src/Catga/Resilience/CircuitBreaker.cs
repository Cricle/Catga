using Microsoft.Extensions.Logging;

namespace Catga.Resilience;

/// <summary>
/// Lock-free circuit breaker implementation to prevent cascading failures.
/// Uses three states: Closed (normal), Open (failing), HalfOpen (testing recovery).
/// </summary>
public sealed class CircuitBreaker : IDisposable
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly ILogger? _logger;

    // Lock-free design using Interlocked for safe concurrent access
    private int _consecutiveFailures;
    private long _lastFailureTimeTicks;
    private int _state; // 0=Closed, 1=Open, 2=HalfOpen

    public CircuitBreaker(
        int failureThreshold = 5,
        TimeSpan? openDuration = null,
        ILogger? logger = null)
    {
        if (failureThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold), "Failure threshold must be greater than 0");

        _failureThreshold = failureThreshold;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(30);
        _logger = logger;
        _state = (int)CircuitState.Closed;
    }

    /// <summary>Current circuit breaker state</summary>
    public CircuitState State => (CircuitState)Volatile.Read(ref _state);

    /// <summary>Number of consecutive failures</summary>
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <summary>
    /// Execute an operation protected by the circuit breaker.
    /// </summary>
    public async Task ExecuteAsync(Func<Task> operation)
    {
        CheckState();

        try
        {
            await operation().ConfigureAwait(false);
            OnSuccess();
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            throw;
        }
    }

    /// <summary>
    /// Execute an operation with return value protected by the circuit breaker.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        CheckState();

        try
        {
            var result = await operation().ConfigureAwait(false);
            OnSuccess();
            return result;
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            throw;
        }
    }

    private void CheckState()
    {
        var currentState = (CircuitState)Volatile.Read(ref _state);

        if (currentState == CircuitState.Open)
        {
            var lastFailureTicks = Volatile.Read(ref _lastFailureTimeTicks);
            var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - lastFailureTicks);

            if (elapsed >= _openDuration)
            {
                // Try to transition to HalfOpen (only one thread succeeds using CAS)
                var original = Interlocked.CompareExchange(
                    ref _state,
                    (int)CircuitState.HalfOpen,
                    (int)CircuitState.Open);

                if (original == (int)CircuitState.Open)
                {
                    _logger?.LogInformation(
                        "Circuit breaker transitioning to Half-Open state after {Duration}s",
                        _openDuration.TotalSeconds);
                }
            }
            else
            {
                var retryAfter = (_openDuration - elapsed).TotalSeconds;
                throw new CircuitBreakerOpenException(
                    $"Circuit breaker is open. Retry after {retryAfter:F1}s");
            }
        }
    }

    private void OnSuccess()
    {
        var currentState = (CircuitState)Volatile.Read(ref _state);

        if (currentState != CircuitState.Closed)
        {
            Volatile.Write(ref _consecutiveFailures, 0);
            Volatile.Write(ref _state, (int)CircuitState.Closed);
            _logger?.LogInformation("Circuit breaker closed after successful operation");
        }
        else
        {
            // Reset failure count on success even in Closed state
            Volatile.Write(ref _consecutiveFailures, 0);
        }
    }

    private void OnFailure(Exception ex)
    {
        Volatile.Write(ref _lastFailureTimeTicks, DateTime.UtcNow.Ticks);
        var failures = Interlocked.Increment(ref _consecutiveFailures);

        _logger?.LogWarning(ex,
            "Circuit breaker recorded failure #{Failures}/{Threshold}",
            failures, _failureThreshold);

        if (failures >= _failureThreshold)
        {
            // Try to open the circuit (only transition from Closed to Open)
            var original = Interlocked.CompareExchange(
                ref _state,
                (int)CircuitState.Open,
                (int)CircuitState.Closed);

            if (original == (int)CircuitState.Closed)
            {
                _logger?.LogError(
                    "Circuit breaker opened after {Failures} consecutive failures. Will remain open for {Duration}s",
                    failures, _openDuration.TotalSeconds);
            }
        }
    }

    /// <summary>
    /// Manually reset the circuit breaker to Closed state.
    /// </summary>
    public void Reset()
    {
        Volatile.Write(ref _consecutiveFailures, 0);
        Volatile.Write(ref _state, (int)CircuitState.Closed);
        _logger?.LogInformation("Circuit breaker manually reset to Closed state");
    }

    public void Dispose()
    {
        // No resources to dispose, but keep interface for future extensibility
    }
}

/// <summary>Circuit breaker state</summary>
public enum CircuitState
{
    /// <summary>Normal operation, requests are allowed</summary>
    Closed = 0,

    /// <summary>Circuit is open, requests are rejected</summary>
    Open = 1,

    /// <summary>Testing recovery, limited requests allowed</summary>
    HalfOpen = 2
}

/// <summary>Exception thrown when circuit breaker is open</summary>
public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}

