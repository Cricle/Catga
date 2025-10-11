using System.Diagnostics;

namespace Catga.HealthCheck;

/// <summary>
/// Service for performing health checks
/// </summary>
public sealed class HealthCheckService
{
    private readonly IEnumerable<IHealthCheck> _healthChecks;

    public HealthCheckService(IEnumerable<IHealthCheck> healthChecks)
    {
        _healthChecks = healthChecks ?? throw new ArgumentNullException(nameof(healthChecks));
    }

    /// <summary>
    /// Check all registered health checks
    /// </summary>
    public async ValueTask<HealthReport> CheckAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, HealthCheckResult>();
        var overallStatus = HealthStatus.Healthy;

        foreach (var healthCheck in _healthChecks)
        {
            var sw = Stopwatch.StartNew();
            HealthCheckResult result;

            try
            {
                result = await healthCheck.CheckAsync(cancellationToken);
                result = result with { Duration = sw.Elapsed };
            }
            catch (Exception ex)
            {
                result = HealthCheckResult.Unhealthy(
                    $"Health check threw an exception: {ex.Message}",
                    ex)
                    with
                { Duration = sw.Elapsed };
            }

            results[healthCheck.Name] = result;

            // Update overall status
            if (result.Status > overallStatus)
            {
                overallStatus = result.Status;
            }
        }

        return new HealthReport(overallStatus, results);
    }

    /// <summary>
    /// Check a specific health check by name
    /// </summary>
    public async ValueTask<HealthCheckResult?> CheckAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var healthCheck = _healthChecks.FirstOrDefault(hc => hc.Name == name);
        if (healthCheck == null)
        {
            return null;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await healthCheck.CheckAsync(cancellationToken);
            return result with { Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Health check threw an exception: {ex.Message}",
                ex)
                with
            { Duration = sw.Elapsed };
        }
    }
}

/// <summary>
/// Report containing all health check results
/// </summary>
public sealed record HealthReport(
    HealthStatus Status,
    IReadOnlyDictionary<string, HealthCheckResult> Entries)
{
    /// <summary>
    /// Total duration of all health checks
    /// </summary>
    public TimeSpan TotalDuration =>
        TimeSpan.FromMilliseconds(Entries.Values.Sum(e => e.Duration.TotalMilliseconds));

    /// <summary>
    /// Whether all checks are healthy
    /// </summary>
    public bool IsHealthy => Status == HealthStatus.Healthy;
}

