using Catga.InMemory.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catga.HealthCheck;

/// <summary>
/// Extension methods for adding health checks to the service collection
/// </summary>
public static class HealthCheckServiceCollectionExtensions
{
    /// <summary>
    /// Add health check service
    /// </summary>
    public static IServiceCollection AddCatgaHealthChecks(
        this IServiceCollection services)
    {
        services.AddSingleton<HealthCheckService>();
        return services;
    }

    /// <summary>
    /// Add a health check
    /// </summary>
    public static IServiceCollection AddHealthCheck<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] THealthCheck>(
        this IServiceCollection services)
        where THealthCheck : class, IHealthCheck
    {
        services.AddSingleton<IHealthCheck, THealthCheck>();
        return services;
    }

    /// <summary>
    /// Add Catga framework health check
    /// </summary>
    public static IServiceCollection AddCatgaFrameworkHealthCheck(
        this IServiceCollection services)
    {
        // Use Microsoft's standard health check API
        services.AddHealthChecks()
            .AddCheck<CatgaHealthCheck>(
                "catga",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: new[] { "catga", "framework" });
        return services;
    }
}

