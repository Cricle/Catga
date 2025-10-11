using Catga.InMemory.Observability;
using Catga.Observability;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Catga health check configuration options
/// </summary>
public class CatgaHealthCheckOptions
{
    /// <summary>
    /// Timeout in seconds for health checks
    /// </summary>
    public int TimeoutSeconds { get; set; } = 5;
}

/// <summary>
/// Catga observability extension methods
/// </summary>
public static class CatgaObservabilityExtensions
{
    /// <summary>
    /// Add complete Catga observability support
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureHealth">Health check configuration</param>
    /// <returns>Service collection</returns>
    public static IServiceCollection AddCatgaObservability(
        this IServiceCollection services,
        Action<CatgaHealthCheckOptions>? configureHealth = null)
    {
        // Add health checks
        services.AddCatgaHealthChecks(configureHealth);

        // Metrics and Tracing are static and automatically enabled
        // Just need to ensure OpenTelemetry is configured correctly

        return services;
    }

    /// <summary>
    /// Add Catga health checks
    /// </summary>
    public static IServiceCollection AddCatgaHealthChecks(
        this IServiceCollection services,
        Action<CatgaHealthCheckOptions>? configure = null)
    {
        var options = new CatgaHealthCheckOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);

        services.AddHealthChecks()
            .AddCheck<CatgaHealthCheck>(
                "catga",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "catga", "framework", "ready" },
                timeout: TimeSpan.FromSeconds(options.TimeoutSeconds));

        return services;
    }

    /// <summary>
    /// Configure OpenTelemetry to integrate with Catga
    /// </summary>
    /// <remarks>
    /// Usage:
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(tracing => tracing.AddCatgaInstrumentation())
    ///     .WithMetrics(metrics => metrics.AddCatgaInstrumentation());
    /// </code>
    /// </remarks>
    public static IServiceCollection AddCatgaOpenTelemetry(
        this IServiceCollection services)
    {
        // Register OpenTelemetry sources
        // Actual OpenTelemetry configuration is done by user in Program.cs
        // This is just a placeholder to show how to integrate

        return services;
    }
}

/// <summary>
/// OpenTelemetry integration extensions (for external OpenTelemetry configuration)
/// </summary>
public static class CatgaOpenTelemetryExtensions
{
    /// <summary>
    /// Add Catga tracing instrumentation
    /// </summary>
    public static object AddCatgaInstrumentation(this object builder)
    {
        // Assuming builder is TracerProviderBuilder
        // builder.AddSource("Catga");
        return builder;
    }

    /// <summary>
    /// Add Catga metrics instrumentation
    /// </summary>
    public static object AddCatgaMetrics(this object builder)
    {
        // Assuming builder is MeterProviderBuilder
        // builder.AddMeter("Catga");
        return builder;
    }
}

