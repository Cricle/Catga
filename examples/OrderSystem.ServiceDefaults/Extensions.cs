using Catga.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
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

        // Add tracing with complete Catga support for distributed systems
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    // ASP.NET Core instrumentation with full context
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = httpContext =>
                        {
                            // Don't trace health checks
                            return !httpContext.Request.Path.StartsWithSegments("/health");
                        };
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            activity.SetTag("http.request.id", request.HttpContext.TraceIdentifier);
                            activity.SetTag("http.request.method", request.Method);
                            activity.SetTag("http.request.path", request.Path);

                            // Propagate correlation ID from header if present
                            if (request.HttpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
                            {
                                activity.SetTag("catga.correlation_id", correlationId.ToString());
                                activity.SetBaggage("catga.correlation_id", correlationId.ToString());
                            }
                        };
                        options.EnrichWithHttpResponse = (activity, response) =>
                        {
                            activity.SetTag("http.response.status_code", response.StatusCode);
                        };
                    })
                    // HTTP Client instrumentation for outgoing requests
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            activity.SetTag("http.request.url", request.RequestUri?.ToString());
                        };
                        options.EnrichWithHttpResponseMessage = (activity, response) =>
                        {
                            activity.SetTag("http.response.status_code", (int)response.StatusCode);
                        };
                    })
                    // ✅ Critical: Add Catga's ActivitySource for distributed tracing
                    .AddSource("Catga.Framework")  // Catga main source
                    .AddSource("Catga.*");          // All Catga sources
            })
            .WithMetrics(metrics =>
            {
                metrics
                    // ASP.NET Core metrics
                    .AddAspNetCoreInstrumentation()
                    // HTTP Client metrics
                    .AddHttpClientInstrumentation()
                    // Runtime metrics (GC, ThreadPool, etc.)
                    .AddRuntimeInstrumentation()
                    // ✅ Catga metrics
                    .AddMeter("Catga.*");
            });

        // Service discovery with resilience + CorrelationId propagation
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // ✅ Critical: Add CorrelationId propagation handler FIRST
            // This ensures X-Correlation-ID is injected before the request is sent
            http.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();
            
            http.AddStandardResilienceHandler();  // Retry, circuit breaker, timeout
            http.AddServiceDiscovery();            // Service discovery
        });
        
        // Register the handler as a transient service
        builder.Services.AddTransient<CorrelationIdDelegatingHandler>();

        // Comprehensive health checks
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live", "ready" });

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

