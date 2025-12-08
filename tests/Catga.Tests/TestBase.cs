namespace Catga.Tests;

/// <summary>
/// Base class for tests with common timeout utilities.
/// </summary>
public abstract class TestBase
{
    /// <summary>Default timeout for async operations in tests.</summary>
    protected static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Short timeout for quick operations.</summary>
    protected static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Wait for a task with timeout. Throws TimeoutException if exceeded.
    /// </summary>
    protected static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan? timeout = null)
    {
        var timeoutValue = timeout ?? DefaultTimeout;
        using var cts = new CancellationTokenSource(timeoutValue);
        try
        {
            return await task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Operation timed out after {timeoutValue.TotalSeconds}s");
        }
    }

    /// <summary>
    /// Wait for a task with timeout. Throws TimeoutException if exceeded.
    /// </summary>
    protected static async Task WithTimeout(Task task, TimeSpan? timeout = null)
    {
        var timeoutValue = timeout ?? DefaultTimeout;
        using var cts = new CancellationTokenSource(timeoutValue);
        try
        {
            await task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Operation timed out after {timeoutValue.TotalSeconds}s");
        }
    }

    /// <summary>
    /// Wait for a condition to become true with timeout.
    /// </summary>
    protected static async Task WaitForCondition(Func<bool> condition, TimeSpan? timeout = null, int pollIntervalMs = 50)
    {
        var timeoutValue = timeout ?? DefaultTimeout;
        var deadline = DateTime.UtcNow + timeoutValue;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Condition not met within {timeoutValue.TotalSeconds}s");
            await Task.Delay(pollIntervalMs);
        }
    }

    /// <summary>
    /// Wait for an async condition to become true with timeout.
    /// </summary>
    protected static async Task WaitForConditionAsync(Func<Task<bool>> condition, TimeSpan? timeout = null, int pollIntervalMs = 50)
    {
        var timeoutValue = timeout ?? DefaultTimeout;
        var deadline = DateTime.UtcNow + timeoutValue;
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Condition not met within {timeoutValue.TotalSeconds}s");
            await Task.Delay(pollIntervalMs);
        }
    }
}
