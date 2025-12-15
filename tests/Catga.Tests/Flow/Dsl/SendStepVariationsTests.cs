using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for various Send step configurations
/// </summary>
public class SendStepVariationsTests
{
    private class TestState : BaseFlowState
    {
        public string Id { get; set; } = "";
        public int Counter { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record SimpleCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record ComplexCommand(string Id, int Value, DateTime Timestamp) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Basic Send Tests

    [Fact]
    public void Send_SimpleCommand_CreatesSendStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new SimpleCommand(s.Id));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Send);
    }

    [Fact]
    public void Send_ComplexCommand_CreatesSendStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new ComplexCommand(s.Id, s.Counter, DateTime.UtcNow));

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Send);
    }

    [Fact]
    public void Send_WithComputedValues_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new SimpleCommand($"prefix-{s.Id}-suffix"));

        builder.Steps[0].Type.Should().Be(StepType.Send);
    }

    #endregion

    #region Chained Send Tests

    [Fact]
    public void MultipleSends_AllCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new SimpleCommand("1"))
            .Send(s => new SimpleCommand("2"))
            .Send(s => new SimpleCommand("3"));

        builder.Steps.Should().HaveCount(3);
        builder.Steps.All(s => s.Type == StepType.Send).Should().BeTrue();
    }

    [Fact]
    public void Send_ChainedWithTag_TagApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new SimpleCommand("cmd")).Tag("important");

        builder.Steps[0].Tag.Should().Be("important");
    }

    [Fact]
    public void Send_ChainedWithOptional_OptionalApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new SimpleCommand("cmd")).Optional();

        builder.Steps[0].IsOptional.Should().BeTrue();
    }

    #endregion

    #region Send in Branches Tests

    [Fact]
    public void Send_InIfBranch_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Counter > 0)
            .Send(s => new SimpleCommand("in-if"))
        .EndIf();

        builder.Steps[0].ThenBranch.Should().HaveCount(1);
        builder.Steps[0].ThenBranch[0].Type.Should().Be(StepType.Send);
    }

    [Fact]
    public void Send_InSwitchCase_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Counter)
            .Case(1, c => c.Send(s => new SimpleCommand("case-1")))
        .EndSwitch();

        builder.Steps[0].Cases[1].Should().HaveCount(1);
        builder.Steps[0].Cases[1][0].Type.Should().Be(StepType.Send);
    }

    [Fact]
    public void Send_InForEach_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => new[] { "a", "b", "c" })
            .Send((s, item) => new SimpleCommand(item))
        .EndForEach();

        builder.Steps[0].ForEachSteps.Should().HaveCount(1);
    }

    #endregion
}
