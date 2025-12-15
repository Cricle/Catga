using Catga.Observability;
using FluentAssertions;

namespace Catga.Tests.Observability;

/// <summary>
/// Advanced tests for DefaultFlowMetrics
/// </summary>
public class DefaultFlowMetricsAdvancedTests
{
    #region Interface Implementation Tests

    [Fact]
    public void Instance_ShouldImplementIFlowMetrics()
    {
        DefaultFlowMetrics.Instance.Should().BeAssignableTo<IFlowMetrics>();
    }

    [Fact]
    public void Instance_ShouldBeSingleton()
    {
        var instance1 = DefaultFlowMetrics.Instance;
        var instance2 = DefaultFlowMetrics.Instance;

        ReferenceEquals(instance1, instance2).Should().BeTrue();
    }

    #endregion

    #region Flow Recording Tests

    [Fact]
    public void RecordFlowStarted_WithAllParams_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordFlowStarted("OrderFlow", "order-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordFlowStarted_WithNullFlowId_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordFlowStarted("OrderFlow", null);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordFlowCompleted_WithAllParams_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordFlowCompleted("OrderFlow", "order-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordFlowFailed_WithAllParams_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordFlowFailed("OrderFlow", "Connection timeout", "order-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordFlowFailed_WithNullError_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordFlowFailed("OrderFlow", null, "order-123");
        act.Should().NotThrow();
    }

    #endregion

    #region Step Recording Tests

    [Fact]
    public void RecordStepStarted_WithValidParams_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordStepStarted("OrderFlow", 0, "Send");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepStarted_WithDifferentStepTypes_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var stepTypes = new[] { "Send", "Query", "Publish", "If", "Switch", "ForEach", "Delay", "Wait" };

        foreach (var stepType in stepTypes)
        {
            var act = () => metrics.RecordStepStarted("TestFlow", 0, stepType);
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void RecordStepCompleted_WithValidParams_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordStepCompleted("OrderFlow", 0, "Send");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepFailed_WithAllParams_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordStepFailed("OrderFlow", 0, "Send", "Step timeout");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepFailed_WithNullError_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordStepFailed("OrderFlow", 0, "Send", null);
        act.Should().NotThrow();
    }

    #endregion

    #region Duration Recording Tests

    [Fact]
    public void RecordFlowDuration_WithPositiveValue_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordFlowDuration("OrderFlow", 1234.56);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordFlowDuration_WithZero_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordFlowDuration("OrderFlow", 0);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordFlowDuration_WithVeryLargeValue_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordFlowDuration("OrderFlow", 999999999.99);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepDuration_WithValidParams_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordStepDuration("OrderFlow", 0, "Send", 50.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepDuration_WithZero_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordStepDuration("OrderFlow", 0, "Send", 0);
        act.Should().NotThrow();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void RecordFlowStarted_WithEmptyFlowName_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordFlowStarted("", "flow-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepStarted_WithNegativeIndex_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordStepStarted("TestFlow", -1, "Send");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordStepStarted_WithLargeIndex_ShouldNotThrow()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var act = () => metrics.RecordStepStarted("TestFlow", 999999, "Send");
        act.Should().NotThrow();
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public void ConcurrentRecording_ShouldBeThreadSafe()
    {
        var metrics = DefaultFlowMetrics.Instance;
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                metrics.RecordFlowStarted($"Flow{index}", $"flow-{index}");
                metrics.RecordStepStarted($"Flow{index}", 0, "Send");
                metrics.RecordStepCompleted($"Flow{index}", 0, "Send");
                metrics.RecordFlowDuration($"Flow{index}", index * 10);
                metrics.RecordFlowCompleted($"Flow{index}", $"flow-{index}");
            }));
        }

        var act = () => Task.WaitAll(tasks.ToArray());
        act.Should().NotThrow();
    }

    #endregion
}
