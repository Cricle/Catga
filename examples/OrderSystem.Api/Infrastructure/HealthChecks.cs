using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OrderSystem.Api.Infrastructure;

/// <summary>
/// Custom health checks for the OrderSystem API.
/// Demonstrates comprehensive health check patterns.
/// </summary>
public static class HealthCheckExtensions
{
    public static IServiceCollection AddOrderSystemHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var healthChecks = services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<CatgaHealthCheck>("catga", tags: ["ready"]);

        // Add Redis health check if configured
        var redisConnection = configuration["Catga:RedisConnection"];
        if (!string.IsNullOrEmpty(redisConnection) &&
            configuration["Catga:Persistence"]?.ToLower() == "redis")
        {
            // Redis health check would be added here
            // healthChecks.AddRedis(redisConnection, tags: ["ready", "redis"]);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapOrderSystemHealthChecks(this IEndpointRouteBuilder app)
    {
        // Liveness probe - is the app running?
        app.MapHealthChecks("/health/live", new()
        {
            Predicate = check => check.Tags.Contains("live")
        });

        // Readiness probe - is the app ready to serve requests?
        app.MapHealthChecks("/health/ready", new()
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        // Full health check
        app.MapHealthChecks("/health", new()
        {
            ResponseWriter = WriteHealthCheckResponse
        });

        return app;
    }

    private static async Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                exception = e.Value.Exception?.Message
            })
        };

        await context.Response.WriteAsJsonAsync(response);
    }
}

/// <summary>
/// Health check for Catga framework components.
/// </summary>
public class CatgaHealthCheck(IServiceProvider serviceProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify core Catga services are registered
            var mediator = serviceProvider.GetService<Catga.ICatgaMediator>();
            if (mediator == null)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Catga mediator not registered"));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                "Catga framework is operational"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Catga framework check failed",
                exception: ex));
        }
    }
}
