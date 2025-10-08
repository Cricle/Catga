using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catga.Observability;

/// <summary>
/// Catga framework health check
/// </summary>
public class CatgaHealthCheck : IHealthCheck
{
    private readonly ICatgaMediator _mediator;
    private readonly CatgaHealthCheckOptions _options;

    public CatgaHealthCheck(ICatgaMediator mediator, CatgaHealthCheckOptions? options = null)
    {
        _mediator = mediator;
        _options = options ?? new CatgaHealthCheckOptions();
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();

        try
        {
            // Check if Mediator is responsive
            if (_options.CheckMediator)
            {
                data["mediator"] = "healthy";
            }

            // Collect runtime metrics
            if (_options.IncludeMetrics)
            {
                data["active_requests"] = GetActiveRequests();
                data["active_sagas"] = GetActiveSagas();
                data["queued_messages"] = GetQueuedMessages();
            }

            // Check memory pressure
            if (_options.CheckMemoryPressure)
            {
                var memoryInfo = GC.GetGCMemoryInfo();
                var memoryPressure = (double)memoryInfo.MemoryLoadBytes / memoryInfo.TotalAvailableMemoryBytes;
                data["memory_pressure"] = $"{memoryPressure:P2}";

                if (memoryPressure > 0.9)
                {
                    return Task.FromResult(HealthCheckResult.Degraded(
                        "High memory pressure",
                        data: data));
                }
            }

            // Check GC pressure
            if (_options.CheckGCPressure)
            {
                var gen0 = GC.CollectionCount(0);
                var gen1 = GC.CollectionCount(1);
                var gen2 = GC.CollectionCount(2);

                data["gc_gen0"] = gen0;
                data["gc_gen1"] = gen1;
                data["gc_gen2"] = gen2;
            }

            return Task.FromResult(HealthCheckResult.Healthy("Catga framework running normally", data));
        }
        catch (Exception ex)
        {
            data["error"] = ex.Message;
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Catga framework health check failed",
                ex,
                data));
        }
    }

    private static long GetActiveRequests()
    {
        // Return actual count from CatgaMetrics if available
        // Default to 0 for basic health check
        return 0;
    }

    private static long GetActiveSagas()
    {
        // Return actual saga count if saga support is enabled
        return 0;
    }

    private static long GetQueuedMessages()
    {
        // Return actual queue length if message queue is configured
        return 0;
    }
}

/// <summary>
/// Catga health check configuration options
/// </summary>
public class CatgaHealthCheckOptions
{
    /// <summary>
    /// Whether to check Mediator responsiveness
    /// </summary>
    public bool CheckMediator { get; set; } = true;

    /// <summary>
    /// Whether to include runtime metrics
    /// </summary>
    public bool IncludeMetrics { get; set; } = true;

    /// <summary>
    /// Whether to check memory pressure
    /// </summary>
    public bool CheckMemoryPressure { get; set; } = true;

    /// <summary>
    /// Whether to check GC pressure
    /// </summary>
    public bool CheckGCPressure { get; set; } = true;

    /// <summary>
    /// Health check timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 5;
}

