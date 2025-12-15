using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder delay and wait constructs
/// </summary>
public class FlowBuilderDelayWaitTests
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

    #region Delay Tests

    [Fact]
    public void Delay_CreatesDelayStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.FromSeconds(5));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Delay);
    }

    [Fact]
    public void Delay_SetsDuration()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.FromSeconds(10));

        builder.Steps[0].DelayDuration.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Delay_MultipleDelays()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Delay(TimeSpan.FromSeconds(1))
            .Delay(TimeSpan.FromSeconds(2))
            .Delay(TimeSpan.FromSeconds(3));

        builder.Steps.Should().HaveCount(3);
    }

    #endregion

    #region Wait Tests

    [Fact]
    public void Wait_CreatesWaitStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Wait(new WaitCondition("corr-123", "EventType"));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Wait);
    }

    [Fact]
    public void Wait_SetsCondition()
    {
        var condition = new WaitCondition("corr-123", "EventType");
        var builder = new FlowBuilder<TestState>();
        builder.Wait(condition);

        builder.Steps[0].WaitCondition.Should().Be(condition);
    }

    [Fact]
    public void Wait_MultipleWaits()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Wait(new WaitCondition("corr-1", "Event1"))
            .Wait(new WaitCondition("corr-2", "Event2"))
            .Wait(new WaitCondition("corr-3", "Event3"));

        builder.Steps.Should().HaveCount(3);
    }

    #endregion

    #region Combined Tests

    [Fact]
    public void DelayAndWait_Combined()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .Delay(TimeSpan.FromSeconds(5))
            .Wait(new WaitCondition("corr", "Event"))
            .Send(s => new TestCommand("2"));

        builder.Steps.Should().HaveCount(4);
        builder.Steps[1].Type.Should().Be(StepType.Delay);
        builder.Steps[2].Type.Should().Be(StepType.Wait);
    }

    #endregion
}
