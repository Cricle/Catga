using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep branching properties and configurations
/// </summary>
public class FlowStepBranchingTests
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

    #region If Branch Tests

    [Fact]
    public void IfStep_HasThenBranch()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => true)
            .Send(s => new TestCommand("1"))
        .EndIf();

        var ifStep = builder.Steps[0];
        ifStep.ThenBranch.Should().NotBeNull();
        ifStep.ThenBranch.Should().HaveCount(1);
    }

    [Fact]
    public void IfStep_WithElse_HasElseBranch()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => true)
            .Send(s => new TestCommand("1"))
        .Else()
            .Send(s => new TestCommand("2"))
        .EndIf();

        var ifStep = builder.Steps[0];
        ifStep.ElseBranch.Should().NotBeNull();
        ifStep.ElseBranch.Should().HaveCount(1);
    }

    [Fact]
    public void IfStep_WithElseIf_HasElseIfBranches()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => true)
            .Send(s => new TestCommand("1"))
        .ElseIf(s => false)
            .Send(s => new TestCommand("2"))
        .EndIf();

        var ifStep = builder.Steps[0];
        ifStep.ElseIfBranches.Should().NotBeNull();
        ifStep.ElseIfBranches.Should().HaveCount(1);
    }

    #endregion

    #region Switch Branch Tests

    [Fact]
    public void SwitchStep_HasCases()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => 1)
            .Case(1, c => c.Send(s => new TestCommand("1")))
        .EndSwitch();

        var switchStep = builder.Steps[0];
        switchStep.Cases.Should().NotBeNull();
        switchStep.Cases.Should().HaveCount(1);
    }

    [Fact]
    public void SwitchStep_WithDefault_HasDefaultBranch()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => 1)
            .Case(1, c => c.Send(s => new TestCommand("1")))
            .Default(c => c.Send(s => new TestCommand("default")))
        .EndSwitch();

        var switchStep = builder.Steps[0];
        switchStep.DefaultBranch.Should().NotBeNull();
        switchStep.DefaultBranch.Should().HaveCount(1);
    }

    #endregion

    #region ForEach Branch Tests

    [Fact]
    public void ForEachStep_HasForEachSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => new[] { "a", "b" })
            .Send((s, item) => new TestCommand(item))
        .EndForEach();

        var forEachStep = builder.Steps[0];
        forEachStep.ForEachSteps.Should().NotBeNull();
        forEachStep.ForEachSteps.Should().HaveCount(1);
    }

    #endregion
}
