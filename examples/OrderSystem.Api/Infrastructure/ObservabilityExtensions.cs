using System.Diagnostics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OrderSystem.Api.Infrastructure;

/// <summary>
/// OpenTelemetry configuration for distributed tracing and metrics.
/// Demonstrates best practices for observability in Catga applications.
/// </summary>
public static class ObservabilityExtensions
{
    public const string ServiceName = "OrderSystem.Api";
    public const string ServiceVersion = "1.0.0";

    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure OpenTelemetry
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: ServiceName,
                    serviceVersion: ServiceVersion,
                    serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing => tracing
                .AddSource("Catga.Framework")
                .AddSource(ServiceName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                })
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options =>
                {
                    var endpoint = configuration["OpenTelemetry:Endpoint"];
                    if (!string.IsNullOrEmpty(endpoint))
                        options.Endpoint = new Uri(endpoint);
                }))
            .WithMetrics(metrics => metrics
                .AddMeter("Catga.Framework")
                .AddMeter(ServiceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(options =>
                {
                    var endpoint = configuration["OpenTelemetry:Endpoint"];
                    if (!string.IsNullOrEmpty(endpoint))
                        options.Endpoint = new Uri(endpoint);
                }));

        return services;
    }

    /// <summary>
    /// Creates an activity for custom operations.
    /// </summary>
    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return new ActivitySource(ServiceName).StartActivity(name, kind);
    }
}
