using Catga.Flow.Dsl;

namespace Catga.Flow;

/// <summary>
/// Extension methods for WaitCondition.
/// </summary>
public static class WaitConditionExtensions
{
    /// <summary>
    /// Check if the wait condition has timed out.
    /// </summary>
    /// <param name="condition">The wait condition to check.</param>
    /// <param name="now">Optional current time. Defaults to DateTime.UtcNow.</param>
    /// <returns>True if timed out, false otherwise.</returns>
    public static bool IsTimedOut(this WaitCondition condition, DateTime? now = null)
        => (now ?? DateTime.UtcNow) >= condition.CreatedAt + condition.Timeout;

    /// <summary>
    /// Get the timeout timestamp for the wait condition.
    /// </summary>
    public static DateTime GetTimeoutAt(this WaitCondition condition)
        => condition.CreatedAt + condition.Timeout;

    /// <summary>
    /// Get remaining time until timeout.
    /// </summary>
    public static TimeSpan GetRemainingTime(this WaitCondition condition, DateTime? now = null)
    {
        var remaining = condition.GetTimeoutAt() - (now ?? DateTime.UtcNow);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
