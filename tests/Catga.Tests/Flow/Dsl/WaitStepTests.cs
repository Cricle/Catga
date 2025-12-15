using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for Wait step
/// </summary>
public class WaitStepTests
{
    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    #region Wait Creation Tests

    [Fact]
    public void Wait_CreatesWaitStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Wait(new WaitCondition("corr-123", "EventType"));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Wait);
    }

    [Fact]
    public void Wait_WithTimeout_SetsTimeout()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Wait(new WaitCondition("corr-123", "EventType"))
            .Timeout(TimeSpan.FromSeconds(30));

        builder.Steps[0].Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    #endregion

    #region Wait Condition Tests

    [Fact]
    public void Wait_WithValidCondition_Works()
    {
        var condition = new WaitCondition("correlation-id", "MyEvent");
        var builder = new FlowBuilder<TestState>();
        builder.Wait(condition);

        builder.Steps[0].WaitCondition.Should().NotBeNull();
        builder.Steps[0].WaitCondition!.CorrelationId.Should().Be("correlation-id");
    }

    [Fact]
    public void Wait_WithEmptyCorrelationId_IsValid()
    {
        var condition = new WaitCondition("", "EventType");
        var builder = new FlowBuilder<TestState>();
        builder.Wait(condition);

        builder.Steps[0].WaitCondition.Should().NotBeNull();
    }

    #endregion

    #region Multiple Wait Tests

    [Fact]
    public void MultipleWaits_AllCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Wait(new WaitCondition("corr-1", "Event1"))
            .Wait(new WaitCondition("corr-2", "Event2"));

        builder.Steps.Should().HaveCount(2);
        builder.Steps.All(s => s.Type == StepType.Wait).Should().BeTrue();
    }

    #endregion
}
