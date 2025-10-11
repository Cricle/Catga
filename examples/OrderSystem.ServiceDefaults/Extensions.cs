using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Service defaults extensions for Aspire
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Add Aspire service defaults (service discovery, health checks, etc.)
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // Service discovery
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Use service discovery for HTTP clients
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        // Health checks
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" });

        return builder;
    }

    /// <summary>
    /// Map default endpoints for health checks
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health checks
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}

