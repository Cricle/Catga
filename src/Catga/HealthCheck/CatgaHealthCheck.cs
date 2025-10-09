using Catga.Performance;

namespace Catga.HealthCheck;

/// <summary>
/// Health check for Catga framework
/// </summary>
public sealed class CatgaHealthCheck : IHealthCheck
{
    private readonly HandlerCache? _handlerCache;
    private readonly Observability.CatgaMetrics? _metrics;

    public string Name => "Catga";

    public CatgaHealthCheck(
        HandlerCache? handlerCache = null,
        Observability.CatgaMetrics? metrics = null)
    {
        _handlerCache = handlerCache;
        _metrics = metrics;
    }

    public ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();

        // Add handler cache statistics if available
        if (_handlerCache != null)
        {
            var stats = _handlerCache.GetStatistics();
            data["handler_cache_total_requests"] = stats.TotalRequests;
            data["handler_cache_thread_local_hits"] = stats.ThreadLocalHits;
            data["handler_cache_shared_hits"] = stats.SharedCacheHits;
            data["handler_cache_service_provider_calls"] = stats.ServiceProviderCalls;
            data["handler_cache_hit_rate"] = stats.HitRate;
        }

        // Add metrics if available
        if (_metrics != null)
        {
            var snapshot = _metrics.GetSnapshot();
            data["requests_total"] = snapshot.TotalRequests;
            data["requests_success"] = snapshot.SuccessfulRequests;
            data["requests_failed"] = snapshot.FailedRequests;
            data["events_published"] = snapshot.TotalEvents;
        }

        data["status"] = "running";
        data["timestamp"] = DateTime.UtcNow;

        return ValueTask.FromResult(
            HealthCheckResult.Healthy("Catga is running", data));
    }
}

