using FluentAssertions;

namespace Catga.Tests.Observability;

/// <summary>
/// TDD tests for Flow DSL diagnostics - metrics, tracing, logging
/// Following DRY and Open-Closed principle
/// </summary>
public class FlowDiagnosticsTests
{
    #region Flow Metrics Interface Tests (Open-Closed)

    [Fact]
    public void IFlowMetrics_ShouldDefineFlowExecutionCounter()
    {
        // Arrange & Act - Interface should exist with proper methods
        var metricsType = Type.GetType("Catga.Observability.IFlowMetrics, Catga");

        // Assert
        metricsType.Should().NotBeNull("IFlowMetrics interface should exist");
        metricsType!.GetMethod("RecordFlowStarted").Should().NotBeNull();
        metricsType.GetMethod("RecordFlowCompleted").Should().NotBeNull();
        metricsType.GetMethod("RecordFlowFailed").Should().NotBeNull();
    }

    [Fact]
    public void IFlowMetrics_ShouldDefineStepExecutionMethods()
    {
        var metricsType = Type.GetType("Catga.Observability.IFlowMetrics, Catga");

        metricsType.Should().NotBeNull();
        metricsType!.GetMethod("RecordStepStarted").Should().NotBeNull();
        metricsType.GetMethod("RecordStepCompleted").Should().NotBeNull();
        metricsType.GetMethod("RecordStepFailed").Should().NotBeNull();
    }

    [Fact]
    public void IFlowMetrics_ShouldDefineDurationRecording()
    {
        var metricsType = Type.GetType("Catga.Observability.IFlowMetrics, Catga");

        metricsType.Should().NotBeNull();
        metricsType!.GetMethod("RecordFlowDuration").Should().NotBeNull();
        metricsType.GetMethod("RecordStepDuration").Should().NotBeNull();
    }

    #endregion

    #region Flow Diagnostics Static Class Tests (DRY - extends CatgaDiagnostics)

    [Fact]
    public void FlowDiagnostics_ShouldHaveFlowCounters()
    {
        var diagnosticsType = Type.GetType("Catga.Observability.FlowDiagnostics, Catga");

        diagnosticsType.Should().NotBeNull("FlowDiagnostics class should exist");
        diagnosticsType!.GetField("FlowsStarted").Should().NotBeNull();
        diagnosticsType.GetField("FlowsCompleted").Should().NotBeNull();
        diagnosticsType.GetField("FlowsFailed").Should().NotBeNull();
    }

    [Fact]
    public void FlowDiagnostics_ShouldHaveStepCounters()
    {
        var diagnosticsType = Type.GetType("Catga.Observability.FlowDiagnostics, Catga");

        diagnosticsType.Should().NotBeNull();
        diagnosticsType!.GetField("StepsExecuted").Should().NotBeNull();
        diagnosticsType.GetField("StepsSucceeded").Should().NotBeNull();
        diagnosticsType.GetField("StepsFailed").Should().NotBeNull();
    }

    [Fact]
    public void FlowDiagnostics_ShouldHaveHistograms()
    {
        var diagnosticsType = Type.GetType("Catga.Observability.FlowDiagnostics, Catga");

        diagnosticsType.Should().NotBeNull();
        diagnosticsType!.GetField("FlowDuration").Should().NotBeNull();
        diagnosticsType.GetField("StepDuration").Should().NotBeNull();
    }

    [Fact]
    public void FlowDiagnostics_ShouldHaveGauges()
    {
        var diagnosticsType = Type.GetType("Catga.Observability.FlowDiagnostics, Catga");

        diagnosticsType.Should().NotBeNull();
        diagnosticsType!.GetField("ActiveFlows").Should().NotBeNull();
    }

    #endregion

    #region Flow Activity Source Tests (Tracing)

    [Fact]
    public void FlowActivitySource_ShouldExist()
    {
        var activitySourceType = Type.GetType("Catga.Observability.FlowActivitySource, Catga");

        activitySourceType.Should().NotBeNull("FlowActivitySource class should exist");
        activitySourceType!.GetField("Source").Should().NotBeNull();
    }

    [Fact]
    public void FlowActivitySource_ShouldDefineFlowTags()
    {
        var activitySourceType = Type.GetType("Catga.Observability.FlowActivitySource, Catga");

        activitySourceType.Should().NotBeNull();
        var tagsType = activitySourceType!.GetNestedType("Tags");
        tagsType.Should().NotBeNull("Tags nested class should exist");
    }

    #endregion

    #region Flow Logger Tests (Structured Logging)

    [Fact]
    public void FlowLogger_ShouldExist()
    {
        var loggerType = Type.GetType("Catga.Observability.FlowLogger, Catga");

        loggerType.Should().NotBeNull("FlowLogger class should exist");
    }

    [Fact]
    public void FlowLogger_ShouldDefineLogMethods()
    {
        var loggerType = Type.GetType("Catga.Observability.FlowLogger, Catga");

        loggerType.Should().NotBeNull();
        loggerType!.GetMethod("LogFlowStarted").Should().NotBeNull();
        loggerType.GetMethod("LogFlowCompleted").Should().NotBeNull();
        loggerType.GetMethod("LogFlowFailed").Should().NotBeNull();
        loggerType.GetMethod("LogStepStarted").Should().NotBeNull();
        loggerType.GetMethod("LogStepCompleted").Should().NotBeNull();
        loggerType.GetMethod("LogStepFailed").Should().NotBeNull();
    }

    #endregion
}
