using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder step count and structure
/// </summary>
public class FlowBuilderStepCountTests
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

    #region Empty Flow Tests

    [Fact]
    public void EmptyFlow_HasNoSteps()
    {
        var builder = new FlowBuilder<TestState>();

        builder.Steps.Should().BeEmpty();
    }

    [Fact]
    public void EmptyFlow_StepsIsNotNull()
    {
        var builder = new FlowBuilder<TestState>();

        builder.Steps.Should().NotBeNull();
    }

    #endregion

    #region Single Step Tests

    [Fact]
    public void SingleStep_CountIsOne()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("1"));

        builder.Steps.Should().HaveCount(1);
    }

    #endregion

    #region Multiple Steps Tests

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(50)]
    public void MultipleSteps_CountCorrect(int count)
    {
        var builder = new FlowBuilder<TestState>();

        for (int i = 0; i < count; i++)
        {
            builder.Send(s => new TestCommand($"cmd-{i}"));
        }

        builder.Steps.Should().HaveCount(count);
    }

    #endregion

    #region Branch Step Count Tests

    [Fact]
    public void IfBranch_CountsAsOneStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => true)
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"))
        .EndIf();

        builder.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void SwitchBranch_CountsAsOneStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => 1)
            .Case(1, c => c.Send(s => new TestCommand("1")))
            .Case(2, c => c.Send(s => new TestCommand("2")))
        .EndSwitch();

        builder.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void ForEachBranch_CountsAsOneStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => new[] { "a", "b" })
            .Send((s, item) => new TestCommand(item))
        .EndForEach();

        builder.Steps.Should().HaveCount(1);
    }

    #endregion

    #region Mixed Steps Tests

    [Fact]
    public void MixedSteps_CountCorrect()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .If(s => true)
                .Send(s => new TestCommand("2"))
            .EndIf()
            .Send(s => new TestCommand("3"))
            .Switch(s => 1)
                .Case(1, c => c.Send(s => new TestCommand("4")))
            .EndSwitch();

        builder.Steps.Should().HaveCount(4);
    }

    #endregion
}
