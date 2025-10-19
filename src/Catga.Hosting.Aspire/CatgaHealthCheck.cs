using Catga;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting;

/// <summary>
/// Health check for Catga message broker
/// </summary>
public sealed class CatgaHealthCheck : IHealthCheck
{
    private readonly ICatgaMediator _mediator;

    public CatgaHealthCheck(ICatgaMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Simple health check: verify mediator is available
            // In a real scenario, you might want to send a ping message
            if (_mediator == null)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Catga mediator is not initialized"));
            }

            // TODO: Add more sophisticated checks
            // - Check transport connectivity
            // - Check persistence availability
            // - Check message processing stats

            return Task.FromResult(HealthCheckResult.Healthy("Catga is operational"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Catga health check failed", ex));
        }
    }
}

