using Catga.Observability;
using Microsoft.AspNetCore.Mvc;

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
            return new
            {
                FlowMetrics = new
                {
                    ActiveFlows = "See Prometheus /metrics endpoint",
                    Counters = new[]
                    {
                        "catga.flow.started",
                        "catga.flow.completed",
                        "catga.flow.failed",
                        "catga.flow.step.executed",
                        "catga.flow.step.succeeded",
                        "catga.flow.step.failed",
                        "catga.flow.step.skipped",
                        "catga.flow.step.retried"
                    },
                    Histograms = new[]
                    {
                        "catga.flow.duration",
                        "catga.flow.step.duration",
                        "catga.flow.step_count"
                    },
                    Gauges = new[]
                    {
                        "catga.flow.active"
                    }
                },
                TracingTags = new
                {
                    FlowTags = new[]
                    {
                        FlowActivitySource.Tags.FlowName,
                        FlowActivitySource.Tags.FlowId,
                        FlowActivitySource.Tags.FlowStatus,
                        FlowActivitySource.Tags.Duration
                    },
                    StepTags = new[]
                    {
                        FlowActivitySource.Tags.StepIndex,
                        FlowActivitySource.Tags.StepType,
                        FlowActivitySource.Tags.StepTag,
                        FlowActivitySource.Tags.StepStatus
                    },
                    ErrorTags = new[]
                    {
                        FlowActivitySource.Tags.Error,
                        FlowActivitySource.Tags.ErrorType
                    }
                },
                TracingEvents = new[]
                {
                    FlowActivitySource.Events.FlowStarted,
                    FlowActivitySource.Events.FlowCompleted,
                    FlowActivitySource.Events.FlowFailed,
                    FlowActivitySource.Events.StepStarted,
                    FlowActivitySource.Events.StepCompleted,
                    FlowActivitySource.Events.StepFailed
                }
            };
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

            return Results.Ok(new
            {
                Message = "Flow metrics recorded",
                FlowName = flowName,
                FlowId = flowId,
                DurationMs = durationMs,
                Steps = new[] { "Send", "Query" }
            });
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

            return Results.Ok(new
            {
                Message = "Flow failure recorded",
                FlowName = flowName,
                FlowId = flowId,
                Error = error
            });
        }).WithName("DemoRecordFlowFailure");

        // Get Grafana dashboard info
        group.MapGet("/grafana", () =>
        {
            return new
            {
                DashboardLocation = "src/Catga/Observability/GrafanaDashboard.json",
                Panels = new[]
                {
                    "Flow Execution Overview",
                    "Active Flows Gauge",
                    "Success Rate",
                    "Flow Duration Distribution",
                    "Step Analysis",
                    "Top Flows by Execution",
                    "Slowest Steps"
                },
                DataSource = "Prometheus",
                ImportInstructions = new[]
                {
                    "1. Open Grafana > Dashboards > Import",
                    "2. Upload GrafanaDashboard.json",
                    "3. Select Prometheus data source",
                    "4. Click Import"
                }
            };
        }).WithName("GetGrafanaInfo");
    }
}
