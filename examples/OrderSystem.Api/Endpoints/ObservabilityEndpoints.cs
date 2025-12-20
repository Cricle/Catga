using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Endpoints demonstrating Catga Observability features:
/// - CatgaDiagnostics (Metrics)
/// - CatgaActivitySource (Tracing)
/// </summary>
public static class ObservabilityEndpoints
{
    [RequiresDynamicCode("Uses reflection for endpoint mapping")]
    [RequiresUnreferencedCode("Uses reflection for endpoint mapping")]
    public static void MapObservabilityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/observability").WithTags("Observability");

        group.MapGet("/metrics", () => Results.Ok(new MetricsResponse(new Dictionary<string, object>
        {
            { "FlowMetrics", "See Prometheus /metrics endpoint" },
            { "Counters", new[] { "catga.flow.started", "catga.flow.completed", "catga.flow.failed", "catga.messages.published", "catga.events.published" } },
            { "Histograms", new[] { "catga.flow.duration", "catga.flow.step.duration", "catga.command.duration", "catga.pipeline.duration" } },
            { "Gauges", new[] { "catga.flow.active", "catga.messages.active" } }
        }))).WithName("GetObservabilityMetrics");

        group.MapPost("/demo/record-flow", ([FromQuery] string flowName, [FromQuery] double durationMs) =>
        {
            var flowId = Guid.NewGuid().ToString("N");
            // Metrics are recorded internally by Catga framework
            return Results.Ok(new DemoRecordFlowResponse(flowName, flowId, "Flow metrics are recorded automatically by Catga"));
        }).WithName("DemoRecordFlowMetrics");

        group.MapPost("/demo/record-failure", ([FromQuery] string flowName, [FromQuery] string error) =>
        {
            var flowId = Guid.NewGuid().ToString("N");
            // Metrics are recorded internally by Catga framework
            return Results.Ok(new DemoRecordFailureResponse(flowName, flowId, error));
        }).WithName("DemoRecordFlowFailure");

        group.MapGet("/grafana", () => Results.Ok(new MetricsResponse(new Dictionary<string, object>
        {
            { "DashboardLocation", "grafana/dashboards" },
            { "DataSource", "Prometheus" }
        }))).WithName("GetGrafanaInfo");
    }
}
