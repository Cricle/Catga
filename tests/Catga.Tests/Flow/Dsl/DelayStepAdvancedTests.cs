using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Advanced tests for Delay step
/// </summary>
public class DelayStepAdvancedTests
{
    private class TestState : BaseFlowState
    {
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    #region Delay Duration Tests

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(60)]
    [InlineData(300)]
    [InlineData(3600)]
    public void Delay_VariousDurations_AllWork(int seconds)
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.FromSeconds(seconds));

        builder.Steps[0].DelayDuration.Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void Delay_MinValue_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.MinValue);

        builder.Steps[0].DelayDuration.Should().Be(TimeSpan.MinValue);
    }

    [Fact]
    public void Delay_MaxValue_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.MaxValue);

        builder.Steps[0].DelayDuration.Should().Be(TimeSpan.MaxValue);
    }

    #endregion

    #region Delay in Flow Tests

    [Fact]
    public void Delay_BetweenSteps_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .Delay(TimeSpan.FromSeconds(5))
            .Send(s => new TestCommand("2"));

        builder.Steps.Should().HaveCount(3);
        builder.Steps[1].Type.Should().Be(StepType.Delay);
    }

    [Fact]
    public void MultipleDelays_AllCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Delay(TimeSpan.FromSeconds(1))
            .Delay(TimeSpan.FromSeconds(2))
            .Delay(TimeSpan.FromSeconds(3));

        builder.Steps.Should().HaveCount(3);
        builder.Steps.All(s => s.Type == StepType.Delay).Should().BeTrue();
    }

    #endregion

    #region Delay in Branches Tests

    [Fact]
    public void Delay_InIfBranch_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => true)
            .Delay(TimeSpan.FromSeconds(5))
        .EndIf();

        builder.Steps[0].ThenBranch[0].Type.Should().Be(StepType.Delay);
    }

    [Fact]
    public void Delay_InSwitchCase_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => 1)
            .Case(1, c => c.Delay(TimeSpan.FromSeconds(5)))
        .EndSwitch();

        builder.Steps[0].Cases[1][0].Type.Should().Be(StepType.Delay);
    }

    #endregion

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }
}
