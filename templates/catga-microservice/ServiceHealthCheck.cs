using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CatgaMicroservice;

public class ServiceHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Add your custom health check logic here
        var isHealthy = true;

        if (isHealthy)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy("Service is running"));
        }

        return Task.FromResult(
            new HealthCheckResult(
                context.Registration.FailureStatus,
                "Service is unhealthy"));
    }
}

