using Catga.Observability;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring OpenTelemetry with Catga.
/// </summary>
public static class CatgaOpenTelemetryExtensions
{
    /// <summary>
    /// Gets the Catga ActivitySource name for OpenTelemetry tracing configuration.
    /// </summary>
    /// <remarks>
    /// Use this when configuring OpenTelemetry:
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(tracing => tracing
    ///         .AddSource(CatgaOpenTelemetryExtensions.ActivitySourceName)
    ///         .AddOtlpExporter());
    /// </code>
    /// </remarks>
    public const string ActivitySourceName = CatgaActivitySource.SourceName;

    /// <summary>
    /// Gets the Catga Meter name for OpenTelemetry metrics configuration.
    /// </summary>
    /// <remarks>
    /// Use this when configuring OpenTelemetry:
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithMetrics(metrics => metrics
    ///         .AddMeter(CatgaOpenTelemetryExtensions.MeterName)
    ///         .AddOtlpExporter());
    /// </code>
    /// </remarks>
    public const string MeterName = CatgaDiagnostics.MeterName;

    /// <summary>
    /// Gets all Catga source names for OpenTelemetry configuration.
    /// </summary>
    public static string[] GetAllSourceNames() => [ActivitySourceName];

    /// <summary>
    /// Gets all Catga meter names for OpenTelemetry configuration.
    /// </summary>
    public static string[] GetAllMeterNames() => [MeterName];
}
