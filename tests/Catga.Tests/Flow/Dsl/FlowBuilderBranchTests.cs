using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder branching constructs
/// </summary>
public class FlowBuilderBranchTests
{
    private class TestState : BaseFlowState
    {
        public int Value { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region If Branch Tests

    [Fact]
    public void If_CreatesIfStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 10).EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.If);
    }

    [Fact]
    public void If_WithThenBranch()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 10)
            .Send(s => new TestCommand("then"))
        .EndIf();

        builder.Steps[0].ThenBranch.Should().HaveCount(1);
    }

    [Fact]
    public void If_WithElseBranch()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 10)
            .Send(s => new TestCommand("then"))
        .Else()
            .Send(s => new TestCommand("else"))
        .EndIf();

        builder.Steps[0].ElseBranch.Should().HaveCount(1);
    }

    [Fact]
    public void If_WithElseIfBranch()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 20)
            .Send(s => new TestCommand("high"))
        .ElseIf(s => s.Value > 10)
            .Send(s => new TestCommand("medium"))
        .Else()
            .Send(s => new TestCommand("low"))
        .EndIf();

        builder.Steps[0].ElseIfBranches.Should().HaveCount(1);
    }

    #endregion

    #region Switch Branch Tests

    [Fact]
    public void Switch_CreatesSwitchStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Value).EndSwitch();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Switch);
    }

    [Fact]
    public void Switch_WithCases()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Value)
            .Case(1, c => c.Send(s => new TestCommand("one")))
            .Case(2, c => c.Send(s => new TestCommand("two")))
        .EndSwitch();

        builder.Steps[0].Cases.Should().HaveCount(2);
    }

    [Fact]
    public void Switch_WithDefault()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Value)
            .Case(1, c => c.Send(s => new TestCommand("one")))
            .Default(c => c.Send(s => new TestCommand("default")))
        .EndSwitch();

        builder.Steps[0].DefaultBranch.Should().HaveCount(1);
    }

    #endregion
}
