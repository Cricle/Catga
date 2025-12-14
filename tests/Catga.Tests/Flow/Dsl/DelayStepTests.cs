using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

public class DelayStepTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    [Fact]
    public void Delay_CreatesDelayStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.FromSeconds(5));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Delay);
    }

    [Fact]
    public void Delay_SetsDelayDuration()
    {
        var builder = new FlowBuilder<TestState>();
        var delay = TimeSpan.FromMinutes(1);
        builder.Delay(delay);

        builder.Steps[0].DelayDuration.Should().Be(delay);
    }

    [Fact]
    public void Delay_ChainedWithSend_AddsMultipleSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("before"))
            .Delay(TimeSpan.FromSeconds(10))
            .Send(s => new TestCommand("after"));

        builder.Steps.Should().HaveCount(3);
        builder.Steps[1].Type.Should().Be(StepType.Delay);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    public void Delay_VariousDurations_Work(int seconds)
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.FromSeconds(seconds));

        builder.Steps[0].DelayDuration.Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void Delay_ZeroDuration_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Delay(TimeSpan.Zero);

        builder.Steps[0].DelayDuration.Should().Be(TimeSpan.Zero);
    }
}
