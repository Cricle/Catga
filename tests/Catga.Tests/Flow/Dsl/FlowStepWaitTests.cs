using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep wait configuration
/// </summary>
public class FlowStepWaitTests
{
    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Wait Step Tests

    [Fact]
    public void WaitStep_CanBeCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Wait(new WaitCondition("corr-123", "EventType"));

        var waitStep = builder.Steps[0];
        waitStep.Type.Should().Be(StepType.Wait);
    }

    [Fact]
    public void WaitStep_HasWaitCondition()
    {
        var condition = new WaitCondition("corr-123", "EventType");
        var builder = new FlowBuilder<TestState>();
        builder.Wait(condition);

        var waitStep = builder.Steps[0];
        waitStep.WaitCondition.Should().Be(condition);
    }

    [Fact]
    public void WaitStep_CanHaveTimeout()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Wait(new WaitCondition("corr-123", "EventType"));
        builder.Steps[0].Timeout = TimeSpan.FromSeconds(30);

        var waitStep = builder.Steps[0];
        waitStep.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    #endregion

    #region Multiple Waits Tests

    [Fact]
    public void MultipleWaits_CanBeChained()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Wait(new WaitCondition("corr-1", "Event1"))
            .Wait(new WaitCondition("corr-2", "Event2"))
            .Wait(new WaitCondition("corr-3", "Event3"));

        builder.Steps.Should().HaveCount(3);
        builder.Steps[0].Type.Should().Be(StepType.Wait);
        builder.Steps[1].Type.Should().Be(StepType.Wait);
        builder.Steps[2].Type.Should().Be(StepType.Wait);
    }

    #endregion

    #region Wait Condition Tests

    [Fact]
    public void WaitCondition_HasCorrelationId()
    {
        var condition = new WaitCondition("corr-123", "EventType");

        condition.CorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public void WaitCondition_HasEventType()
    {
        var condition = new WaitCondition("corr-123", "EventType");

        condition.EventType.Should().Be("EventType");
    }

    #endregion
}
