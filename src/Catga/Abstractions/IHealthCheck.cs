namespace Catga.HealthCheck;

/// <summary>
/// Health check interface
/// </summary>
public interface IHealthCheck
{
    /// <summary>
    /// Name of this health check
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Perform the health check
    /// </summary>
    public ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a health check
/// </summary>
public sealed record HealthCheckResult
{
    public HealthStatus Status { get; init; }
    public string? Description { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyDictionary<string, object>? Data { get; init; }
    public Exception? Exception { get; init; }

    public static HealthCheckResult Healthy(
        string? description = null,
        IReadOnlyDictionary<string, object>? data = null)
    {
        return new HealthCheckResult
        {
            Status = HealthStatus.Healthy,
            Description = description,
            Data = data
        };
    }

    public static HealthCheckResult Degraded(
        string? description = null,
        IReadOnlyDictionary<string, object>? data = null)
    {
        return new HealthCheckResult
        {
            Status = HealthStatus.Degraded,
            Description = description,
            Data = data
        };
    }

    public static HealthCheckResult Unhealthy(
        string? description = null,
        Exception? exception = null,
        IReadOnlyDictionary<string, object>? data = null)
    {
        return new HealthCheckResult
        {
            Status = HealthStatus.Unhealthy,
            Description = description,
            Exception = exception,
            Data = data
        };
    }
}

/// <summary>
/// Health status enum
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// Component is healthy
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// Component is degraded but still functional
    /// </summary>
    Degraded = 1,

    /// <summary>
    /// Component is unhealthy
    /// </summary>
    Unhealthy = 2
}

