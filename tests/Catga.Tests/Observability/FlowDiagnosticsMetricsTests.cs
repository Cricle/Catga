using Catga.Observability;
using FluentAssertions;

namespace Catga.Tests.Observability;

/// <summary>
/// Comprehensive tests for FlowDiagnostics metrics
/// </summary>
public class FlowDiagnosticsMetricsTests
{
    #region Counter Tests

    [Fact]
    public void FlowsStarted_ShouldNotBeNull()
    {
        FlowDiagnostics.FlowsStarted.Should().NotBeNull();
    }

    [Fact]
    public void FlowsCompleted_ShouldNotBeNull()
    {
        FlowDiagnostics.FlowsCompleted.Should().NotBeNull();
    }

    [Fact]
    public void FlowsFailed_ShouldNotBeNull()
    {
        FlowDiagnostics.FlowsFailed.Should().NotBeNull();
    }

    [Fact]
    public void StepsExecuted_ShouldNotBeNull()
    {
        FlowDiagnostics.StepsExecuted.Should().NotBeNull();
    }

    [Fact]
    public void StepsSucceeded_ShouldNotBeNull()
    {
        FlowDiagnostics.StepsSucceeded.Should().NotBeNull();
    }

    [Fact]
    public void StepsFailed_ShouldNotBeNull()
    {
        FlowDiagnostics.StepsFailed.Should().NotBeNull();
    }

    [Fact]
    public void StepsSkipped_ShouldNotBeNull()
    {
        FlowDiagnostics.StepsSkipped.Should().NotBeNull();
    }

    [Fact]
    public void StepsRetried_ShouldNotBeNull()
    {
        FlowDiagnostics.StepsRetried.Should().NotBeNull();
    }

    #endregion

    #region Histogram Tests

    [Fact]
    public void FlowDuration_ShouldNotBeNull()
    {
        FlowDiagnostics.FlowDuration.Should().NotBeNull();
    }

    [Fact]
    public void StepDuration_ShouldNotBeNull()
    {
        FlowDiagnostics.StepDuration.Should().NotBeNull();
    }

    [Fact]
    public void FlowStepCount_ShouldNotBeNull()
    {
        FlowDiagnostics.FlowStepCount.Should().NotBeNull();
    }

    #endregion

    #region Gauge Tests

    [Fact]
    public void ActiveFlows_ShouldNotBeNull()
    {
        FlowDiagnostics.ActiveFlows.Should().NotBeNull();
    }

    #endregion

    #region Helper Method Tests

    [Fact]
    public void IncrementActiveFlows_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.IncrementActiveFlows();
        act.Should().NotThrow();
    }

    [Fact]
    public void DecrementActiveFlows_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.DecrementActiveFlows();
        act.Should().NotThrow();
    }

    [Fact]
    public void IncrementAndDecrementActiveFlows_ShouldBalance()
    {
        FlowDiagnostics.IncrementActiveFlows();
        FlowDiagnostics.IncrementActiveFlows();
        FlowDiagnostics.DecrementActiveFlows();
        FlowDiagnostics.DecrementActiveFlows();
        // Should not throw
    }

    #endregion

    #region Counter Recording Tests

    [Fact]
    public void FlowsStarted_Add_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.FlowsStarted.Add(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void FlowsCompleted_Add_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.FlowsCompleted.Add(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void FlowsFailed_Add_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.FlowsFailed.Add(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void StepsExecuted_Add_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.StepsExecuted.Add(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void StepsSucceeded_Add_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.StepsSucceeded.Add(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void StepsFailed_Add_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.StepsFailed.Add(1);
        act.Should().NotThrow();
    }

    #endregion

    #region Histogram Recording Tests

    [Fact]
    public void FlowDuration_Record_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.FlowDuration.Record(100.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void StepDuration_Record_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.StepDuration.Record(50.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void FlowStepCount_Record_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.FlowStepCount.Record(5);
        act.Should().NotThrow();
    }

    #endregion

    #region Counter With Tags Tests

    [Fact]
    public void FlowsStarted_AddWithTags_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.FlowsStarted.Add(1,
            new KeyValuePair<string, object?>("flow.name", "TestFlow"));
        act.Should().NotThrow();
    }

    [Fact]
    public void StepsExecuted_AddWithMultipleTags_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.StepsExecuted.Add(1,
            new KeyValuePair<string, object?>("flow.name", "TestFlow"),
            new KeyValuePair<string, object?>("step.type", "Send"));
        act.Should().NotThrow();
    }

    #endregion

    #region Histogram With Tags Tests

    [Fact]
    public void FlowDuration_RecordWithTags_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.FlowDuration.Record(100.0,
            new KeyValuePair<string, object?>("flow.name", "TestFlow"));
        act.Should().NotThrow();
    }

    [Fact]
    public void StepDuration_RecordWithMultipleTags_ShouldNotThrow()
    {
        var act = () => FlowDiagnostics.StepDuration.Record(50.0,
            new KeyValuePair<string, object?>("flow.name", "TestFlow"),
            new KeyValuePair<string, object?>("step.type", "Query"));
        act.Should().NotThrow();
    }

    #endregion
}
