using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Service defaults extensions for Aspire - comprehensive observability and resilience
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Add Aspire service defaults with full observability
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // Configure OpenTelemetry
        builder.AddOpenTelemetryExporters();

        // Add tracing
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Catga.*");  // Catga tracing
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Catga.*");  // Catga metrics
            });

        // Service discovery with resilience
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();  // Retry, circuit breaker, timeout
            http.AddServiceDiscovery();            // Service discovery
        });

        // Comprehensive health checks
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live", "ready" });
        
        // Register Catga Debugger health check if DebuggerHealthCheck is available
        builder.Services.TryAddSingleton<Catga.Debugger.HealthChecks.DebuggerHealthCheck>();
        builder.Services.AddHealthChecks()
            .AddCheck<Catga.Debugger.HealthChecks.DebuggerHealthCheck>(
                "catga-debugger",
                tags: new[] { "ready", "catga" });

        // Default logging
        builder.Services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.AddDebug();
        });

        return builder;
    }

    /// <summary>
    /// Add OpenTelemetry exporters for Aspire Dashboard
    /// </summary>
    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Map default endpoints - health, metrics, etc.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Liveness probe - is the app running?
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        // Readiness probe - is the app ready to serve traffic?
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready")
        });

        // Combined health check
        app.MapHealthChecks("/health");

        return app;
    }
}

