using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding Catga health checks
/// </summary>
public static class CatgaHealthCheckExtensions
{
    /// <summary>
    /// Adds Catga health check to the service collection
    /// </summary>
    public static IHealthChecksBuilder AddCatgaHealthCheck(
        this IHealthChecksBuilder builder,
        string? name = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name ?? "catga",
            sp => new CatgaHealthCheck(sp.GetRequiredService<Catga.ICatgaMediator>()),
            failureStatus,
            tags,
            timeout));
    }
}

