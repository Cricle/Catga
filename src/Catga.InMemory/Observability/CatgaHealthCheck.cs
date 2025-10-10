using Microsoft.Extensions.Diagnostics.HealthChecks;
using Catga.Performance;
using Catga.Observability;

namespace Catga.InMemory.Observability;

/// <summary>
/// Catga framework health check
/// </summary>
public class CatgaHealthCheck : IHealthCheck
{
    private readonly HandlerCache _handlerCache;
    private readonly CatgaMetrics _metrics;

    public CatgaHealthCheck(HandlerCache handlerCache, CatgaMetrics metrics)
    {
        _handlerCache = handlerCache;
        _metrics = metrics;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check handler cache statistics
            var stats = _handlerCache.GetStatistics();
            var missRate = 1.0 - stats.HitRate;
            var data = new Dictionary<string, object>
            {
                ["cache_hit_rate"] = stats.HitRate,
                ["cache_miss_rate"] = missRate,
                ["total_requests"] = _metrics.TotalRequests,
                ["failed_requests"] = _metrics.FailedRequests,
                ["framework_status"] = "healthy"
            };

            // Determine health status
            var isHealthy = stats.HitRate > 0.5 || _metrics.TotalRequests == 0;

            return Task.FromResult(isHealthy
                ? HealthCheckResult.Healthy("Catga framework is healthy", data)
                : HealthCheckResult.Degraded("Catga framework performance degraded", null, data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Catga framework health check failed", ex));
        }
    }
}

