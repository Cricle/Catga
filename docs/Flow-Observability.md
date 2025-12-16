# Flow Observability

Comprehensive observability for Flow DSL with metrics, tracing, and structured logging.

## Features

- **Metrics**: Counters, histograms, and gauges via OpenTelemetry
- **Distributed Tracing**: Activity-based tracing with rich tags
- **Structured Logging**: Consistent log messages for all flow events
- **Grafana Dashboard**: Pre-built dashboard for visualization

## Components

### FlowDiagnostics (Metrics)

```csharp
// Counters
FlowDiagnostics.FlowsStarted.Add(1, new("flow.name", flowName));
FlowDiagnostics.FlowsCompleted.Add(1);
FlowDiagnostics.FlowsFailed.Add(1);
FlowDiagnostics.StepsExecuted.Add(1);

// Histograms
FlowDiagnostics.FlowDuration.Record(elapsedMs);
FlowDiagnostics.StepDuration.Record(stepMs);

// Gauges
FlowDiagnostics.IncrementActiveFlows();
FlowDiagnostics.DecrementActiveFlows();
```

### FlowActivitySource (Tracing)

```csharp
using var activity = FlowActivitySource.Source.StartActivity("Flow.OrderFlow");
activity?.SetTag(FlowActivitySource.Tags.FlowId, flowId);
activity?.SetTag(FlowActivitySource.Tags.FlowName, flowName);
activity?.AddEvent(new ActivityEvent(FlowActivitySource.Events.FlowStarted));
```

#### Available Tags

| Tag | Description |
|-----|-------------|
| `catga.flow.name` | Flow name |
| `catga.flow.id` | Flow instance ID |
| `catga.flow.status` | Flow status (running/completed/failed) |
| `catga.flow.step.index` | Current step index |
| `catga.flow.step.type` | Step type (Send/Query/If/etc) |
| `catga.flow.error` | Error message |
| `catga.flow.duration.ms` | Execution duration |

#### Available Events

| Event | Description |
|-------|-------------|
| `catga.flow.started` | Flow execution started |
| `catga.flow.completed` | Flow completed successfully |
| `catga.flow.failed` | Flow failed with error |
| `catga.flow.step.started` | Step started |
| `catga.flow.step.completed` | Step completed |

### FlowLogger (Structured Logging)

```csharp
FlowLogger.LogFlowStarted(logger, "OrderFlow", flowId);
FlowLogger.LogStepStarted(logger, "OrderFlow", stepIndex, "Send", "payment");
FlowLogger.LogStepCompleted(logger, "OrderFlow", stepIndex, "Send", durationMs);
FlowLogger.LogFlowCompleted(logger, "OrderFlow", totalDurationMs, flowId);
```

### IFlowMetrics (Interface)

```csharp
public interface IFlowMetrics
{
    void RecordFlowStarted(string flowName, string? flowId = null);
    void RecordFlowCompleted(string flowName, string? flowId = null);
    void RecordFlowFailed(string flowName, string? error = null, string? flowId = null);
    void RecordStepStarted(string flowName, int stepIndex, string stepType);
    void RecordStepCompleted(string flowName, int stepIndex, string stepType);
    void RecordStepFailed(string flowName, int stepIndex, string stepType, string? error = null);
    void RecordFlowDuration(string flowName, double durationMs);
    void RecordStepDuration(string flowName, int stepIndex, string stepType, double durationMs);
}
```

## Grafana Dashboard

Import the pre-built dashboard from `src/Catga/Observability/GrafanaDashboard.json`.

### Panels Included

- Flow Execution Overview (started/completed/failed)
- Active Flows Gauge
- Success Rate
- Flow Duration Distribution
- Step Analysis
- Top Flows by Execution Count
- Slowest Steps

## OpenTelemetry Integration

```csharp
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("Catga.Flow"))
    .WithTracing(tracing => tracing
        .AddSource("Catga.Flow"));
```

## Best Practices

1. **Use tags consistently** for filtering and grouping
2. **Record durations** for performance analysis
3. **Track active flows** to monitor concurrency
4. **Import the Grafana dashboard** for instant visibility
