using Catga.Observability;
using Microsoft.AspNetCore.Mvc;
using OrderSystem.Api;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Endpoints demonstrating Catga Observability features:
/// - FlowDiagnostics (Metrics)
/// - FlowActivitySource (Tracing)
/// - FlowLogger (Structured Logging)
/// </summary>
public static class ObservabilityEndpoints
{
    public static void MapObservabilityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/observability")
            .WithTags("Observability");

        // Get current metrics snapshot
        group.MapGet("/metrics", () =>
        {
            return Results.Ok(new MetricsResponse(new Dictionary<string, object>
            {
                { "FlowMetrics", "See Prometheus /metrics endpoint" },
                { "Counters", new[] { "catga.flow.started", "catga.flow.completed", "catga.flow.failed" } },
                { "Histograms", new[] { "catga.flow.duration", "catga.flow.step.duration" } },
                { "Gauges", new[] { "catga.flow.active" } }
            }));
        }).WithName("GetObservabilityMetrics");

        // Demo: Record custom metrics
        group.MapPost("/demo/record-flow", ([FromQuery] string flowName, [FromQuery] double durationMs) =>
        {
            var metrics = DefaultFlowMetrics.Instance;
            var flowId = Guid.NewGuid().ToString("N");

            // Record flow lifecycle
            metrics.RecordFlowStarted(flowName, flowId);
            metrics.RecordStepStarted(flowName, 0, "Send");
            metrics.RecordStepCompleted(flowName, 0, "Send");
            metrics.RecordStepDuration(flowName, 0, "Send", durationMs * 0.3);
            metrics.RecordStepStarted(flowName, 1, "Query");
            metrics.RecordStepCompleted(flowName, 1, "Query");
            metrics.RecordStepDuration(flowName, 1, "Query", durationMs * 0.7);
            metrics.RecordFlowDuration(flowName, durationMs);
            metrics.RecordFlowCompleted(flowName, flowId);

            return Results.Ok(new DemoRecordFlowResponse(flowName, flowId, "Flow metrics recorded"));
        }).WithName("DemoRecordFlowMetrics");

        // Demo: Simulate flow failure
        group.MapPost("/demo/record-failure", ([FromQuery] string flowName, [FromQuery] string error) =>
        {
            var metrics = DefaultFlowMetrics.Instance;
            var flowId = Guid.NewGuid().ToString("N");

            metrics.RecordFlowStarted(flowName, flowId);
            metrics.RecordStepStarted(flowName, 0, "Send");
            metrics.RecordStepFailed(flowName, 0, "Send", error);
            metrics.RecordFlowFailed(flowName, error, flowId);

            return Results.Ok(new DemoRecordFailureResponse(flowName, flowId, error));
        }).WithName("DemoRecordFlowFailure");

        // Get Grafana dashboard info
        group.MapGet("/grafana", () =>
        {
            return Results.Ok(new MetricsResponse(new Dictionary<string, object>
            {
                { "DashboardLocation", "src/Catga/Observability/GrafanaDashboard.json" },
                { "DataSource", "Prometheus" }
            }));
        }).WithName("GetGrafanaInfo");
    }
}
