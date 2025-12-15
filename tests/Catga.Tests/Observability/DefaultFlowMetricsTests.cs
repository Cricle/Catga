using Catga.Observability;
using FluentAssertions;

namespace Catga.Tests.Observability;

/// <summary>
/// Tests for DefaultFlowMetrics implementation
/// </summary>
public class DefaultFlowMetricsTests
{
    [Fact]
    public void Instance_ShouldBeSingleton()
    {
        var instance1 = DefaultFlowMetrics.Instance;
        var instance2 = DefaultFlowMetrics.Instance;

        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public void RecordFlowStarted_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;

        var act = () => metrics.RecordFlowStarted("TestFlow", "flow-123");

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordFlowCompleted_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;

        var act = () => metrics.RecordFlowCompleted("TestFlow", "flow-123");

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordFlowFailed_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;

        var act = () => metrics.RecordFlowFailed("TestFlow", "Some error", "flow-123");

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepStarted_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;

        var act = () => metrics.RecordStepStarted("TestFlow", 0, "Send");

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepCompleted_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;

        var act = () => metrics.RecordStepCompleted("TestFlow", 0, "Send");

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepFailed_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;

        var act = () => metrics.RecordStepFailed("TestFlow", 0, "Send", "Step error");

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordFlowDuration_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;

        var act = () => metrics.RecordFlowDuration("TestFlow", 123.45);

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepDuration_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;

        var act = () => metrics.RecordStepDuration("TestFlow", 0, "Send", 45.67);

        act.Should().NotThrow();
    }

    [Fact]
    public void IFlowMetrics_ShouldBeImplemented()
    {
        IFlowMetrics metrics = DefaultFlowMetrics.Instance;

        metrics.Should().NotBeNull();
        metrics.Should().BeAssignableTo<IFlowMetrics>();
    }
}
