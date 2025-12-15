using Catga.Observability;
using FluentAssertions;

namespace Catga.Tests.Observability;

/// <summary>
/// Contract tests for IFlowMetrics interface
/// </summary>
public class IFlowMetricsContractTests
{
    [Fact]
    public void IFlowMetrics_ShouldBeInterface()
    {
        typeof(IFlowMetrics).IsInterface.Should().BeTrue();
    }

    [Fact]
    public void IFlowMetrics_ShouldHaveRecordFlowStartedMethod()
    {
        var method = typeof(IFlowMetrics).GetMethod("RecordFlowStarted");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void IFlowMetrics_ShouldHaveRecordFlowCompletedMethod()
    {
        var method = typeof(IFlowMetrics).GetMethod("RecordFlowCompleted");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void IFlowMetrics_ShouldHaveRecordFlowFailedMethod()
    {
        var method = typeof(IFlowMetrics).GetMethod("RecordFlowFailed");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void IFlowMetrics_ShouldHaveRecordStepStartedMethod()
    {
        var method = typeof(IFlowMetrics).GetMethod("RecordStepStarted");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void IFlowMetrics_ShouldHaveRecordStepCompletedMethod()
    {
        var method = typeof(IFlowMetrics).GetMethod("RecordStepCompleted");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void IFlowMetrics_ShouldHaveRecordStepFailedMethod()
    {
        var method = typeof(IFlowMetrics).GetMethod("RecordStepFailed");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void IFlowMetrics_ShouldHaveRecordFlowDurationMethod()
    {
        var method = typeof(IFlowMetrics).GetMethod("RecordFlowDuration");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void IFlowMetrics_ShouldHaveRecordStepDurationMethod()
    {
        var method = typeof(IFlowMetrics).GetMethod("RecordStepDuration");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(void));
    }

    [Fact]
    public void IFlowMetrics_TotalMethodCount_ShouldBeEight()
    {
        var methods = typeof(IFlowMetrics).GetMethods();
        methods.Should().HaveCount(8);
    }

    [Fact]
    public void DefaultFlowMetrics_ShouldImplementIFlowMetrics()
    {
        typeof(DefaultFlowMetrics).GetInterfaces()
            .Should().Contain(typeof(IFlowMetrics));
    }
}
