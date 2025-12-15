using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for Optional step configuration
/// </summary>
public class OptionalStepTests
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

    #region Optional Configuration Tests

    [Fact]
    public void Optional_SetsIsOptionalTrue()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Optional();

        builder.Steps[0].IsOptional.Should().BeTrue();
    }

    [Fact]
    public void Optional_MultipleSteps_EachMarked()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1")).Optional()
            .Send(s => new TestCommand("2")).Optional()
            .Send(s => new TestCommand("3"));

        builder.Steps[0].IsOptional.Should().BeTrue();
        builder.Steps[1].IsOptional.Should().BeTrue();
        builder.Steps[2].IsOptional.Should().BeFalse();
    }

    #endregion

    #region Optional with Other Modifiers Tests

    [Fact]
    public void Optional_WithTag_BothApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .Optional()
            .Tag("optional-step");

        var step = builder.Steps[0];
        step.IsOptional.Should().BeTrue();
        step.Tag.Should().Be("optional-step");
    }

    [Fact]
    public void Optional_WithOnlyWhen_BothApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .Optional()
            .OnlyWhen(s => true);

        var step = builder.Steps[0];
        step.IsOptional.Should().BeTrue();
        step.Condition.Should().NotBeNull();
    }

    [Fact]
    public void Optional_WithIfFail_BothApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .Optional()
            .IfFail().ContinueFlow();

        var step = builder.Steps[0];
        step.IsOptional.Should().BeTrue();
        step.FailureAction.Should().NotBeNull();
    }

    #endregion

    #region Optional in Branches Tests

    [Fact]
    public void Optional_InIfBranch_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => true)
            .Send(s => new TestCommand("cmd")).Optional()
        .EndIf();

        builder.Steps[0].ThenBranch[0].IsOptional.Should().BeTrue();
    }

    [Fact]
    public void Optional_InSwitchCase_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => 1)
            .Case(1, c => c.Send(s => new TestCommand("cmd")).Optional())
        .EndSwitch();

        builder.Steps[0].Cases[1][0].IsOptional.Should().BeTrue();
    }

    #endregion
}
