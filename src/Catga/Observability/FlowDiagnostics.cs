using System.Diagnostics.Metrics;

namespace Catga.Observability;

/// <summary>
/// Centralized diagnostics for Flow DSL (Metrics).
/// Extends CatgaDiagnostics pattern - DRY principle.
/// </summary>
public static class FlowDiagnostics
{
    private const string MeterName = "Catga.Flow";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    // ========== Counters - Flow Operations ==========

    public static readonly Counter<long> FlowsStarted =
        Meter.CreateCounter<long>("catga.flow.started", "flows", "Total flows started");

    public static readonly Counter<long> FlowsCompleted =
        Meter.CreateCounter<long>("catga.flow.completed", "flows", "Total flows completed successfully");

    public static readonly Counter<long> FlowsFailed =
        Meter.CreateCounter<long>("catga.flow.failed", "flows", "Total flows failed");

    // ========== Counters - Step Operations ==========

    public static readonly Counter<long> StepsExecuted =
        Meter.CreateCounter<long>("catga.flow.step.executed", "steps", "Total steps executed");

    public static readonly Counter<long> StepsSucceeded =
        Meter.CreateCounter<long>("catga.flow.step.succeeded", "steps", "Total steps succeeded");

    public static readonly Counter<long> StepsFailed =
        Meter.CreateCounter<long>("catga.flow.step.failed", "steps", "Total steps failed");

    public static readonly Counter<long> StepsSkipped =
        Meter.CreateCounter<long>("catga.flow.step.skipped", "steps", "Total steps skipped");

    public static readonly Counter<long> StepsRetried =
        Meter.CreateCounter<long>("catga.flow.step.retried", "steps", "Total step retries");

    // ========== Histograms - Duration ==========

    public static readonly Histogram<double> FlowDuration =
        Meter.CreateHistogram<double>("catga.flow.duration", "ms", "Flow execution duration");

    public static readonly Histogram<double> StepDuration =
        Meter.CreateHistogram<double>("catga.flow.step.duration", "ms", "Step execution duration");

    // ========== Histograms - Flow Structure ==========

    public static readonly Histogram<int> FlowStepCount =
        Meter.CreateHistogram<int>("catga.flow.step_count", "steps", "Number of steps in flow");

    // ========== Gauges - Active Flows ==========

    private static long _activeFlows;

    public static readonly ObservableGauge<long> ActiveFlows =
        Meter.CreateObservableGauge("catga.flow.active", () => _activeFlows, "flows", "Active flows being executed");

    // ========== Helper Methods ==========

    public static void IncrementActiveFlows() => Interlocked.Increment(ref _activeFlows);
    public static void DecrementActiveFlows() => Interlocked.Decrement(ref _activeFlows);
}
