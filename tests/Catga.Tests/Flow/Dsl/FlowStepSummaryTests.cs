using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Summary tests for all FlowStep functionality
/// </summary>
public class FlowStepSummaryTests
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

    #region Summary of All Step Features

    [Fact]
    public void FlowStep_Summary_AllFeaturesAvailable()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1")).Tag("api").Optional().OnlyWhen(s => true)
            .Query(s => new TestCommand("2")).Tag("api").IfFail().ContinueFlow()
            .Publish(s => new TestCommand("3")).Tag("api")
            .If(s => true)
                .Send(s => new TestCommand("4"))
            .EndIf()
            .Switch(s => 1)
                .Case(1, c => c.Send(s => new TestCommand("5")))
            .EndSwitch()
            .ForEach(s => new[] { "a" })
                .Send((s, item) => new TestCommand(item))
            .EndForEach()
            .Delay(TimeSpan.FromSeconds(1))
            .Wait(new WaitCondition("corr", "Event"));

        builder.Steps.Should().HaveCount(8);
        builder.Steps[0].Tag.Should().Be("api");
        builder.Steps[0].IsOptional.Should().BeTrue();
        builder.Steps[0].Condition.Should().NotBeNull();
    }

    #endregion

    #region Step Type Verification

    [Fact]
    public void AllStepTypes_AreCorrect()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .Query(s => new TestCommand("2"))
            .Publish(s => new TestCommand("3"))
            .If(s => true).EndIf()
            .Switch(s => 1).EndSwitch()
            .ForEach(s => new[] { "a" }).EndForEach()
            .Delay(TimeSpan.FromSeconds(1))
            .Wait(new WaitCondition("corr", "Event"));

        builder.Steps[0].Type.Should().Be(StepType.Send);
        builder.Steps[1].Type.Should().Be(StepType.Query);
        builder.Steps[2].Type.Should().Be(StepType.Publish);
        builder.Steps[3].Type.Should().Be(StepType.If);
        builder.Steps[4].Type.Should().Be(StepType.Switch);
        builder.Steps[5].Type.Should().Be(StepType.ForEach);
        builder.Steps[6].Type.Should().Be(StepType.Delay);
        builder.Steps[7].Type.Should().Be(StepType.Wait);
    }

    #endregion
}
