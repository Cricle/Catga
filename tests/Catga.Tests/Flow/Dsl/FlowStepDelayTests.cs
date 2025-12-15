using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep delay configuration
/// </summary>
public class FlowStepDelayTests
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

    #region Delay Step Tests

    [Fact]
    public void DelayStep_CanBeCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.FromSeconds(5));

        var delayStep = builder.Steps[0];
        delayStep.Type.Should().Be(StepType.Delay);
    }

    [Fact]
    public void DelayStep_HasDuration()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.FromSeconds(10));

        var delayStep = builder.Steps[0];
        delayStep.DelayDuration.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void DelayStep_CanHaveZeroDuration()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.Zero);

        var delayStep = builder.Steps[0];
        delayStep.DelayDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void DelayStep_CanHaveLargeDuration()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.FromHours(1));

        var delayStep = builder.Steps[0];
        delayStep.DelayDuration.Should().Be(TimeSpan.FromHours(1));
    }

    #endregion

    #region Multiple Delays Tests

    [Fact]
    public void MultipleDelays_CanBeChained()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Delay(TimeSpan.FromSeconds(1))
            .Delay(TimeSpan.FromSeconds(2))
            .Delay(TimeSpan.FromSeconds(3));

        builder.Steps.Should().HaveCount(3);
        builder.Steps[0].DelayDuration.Should().Be(TimeSpan.FromSeconds(1));
        builder.Steps[1].DelayDuration.Should().Be(TimeSpan.FromSeconds(2));
        builder.Steps[2].DelayDuration.Should().Be(TimeSpan.FromSeconds(3));
    }

    #endregion
}
