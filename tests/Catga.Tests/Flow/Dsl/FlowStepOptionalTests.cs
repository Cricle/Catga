using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep optional configuration
/// </summary>
public class FlowStepOptionalTests
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

    #region Optional Step Tests

    [Fact]
    public void Step_CanBeMarkedOptional()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Optional();

        builder.Steps[0].IsOptional.Should().BeTrue();
    }

    [Fact]
    public void Step_CanBeMarkedRequired()
    {
        var builder = new FlowBuilder<TestState>();
        var step = new FlowStep { IsOptional = false };

        step.IsOptional.Should().BeFalse();
    }

    [Fact]
    public void MultipleSteps_CanHaveMixedOptionalStatus()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1")).Optional()
            .Send(s => new TestCommand("2"))
            .Send(s => new TestCommand("3")).Optional();

        builder.Steps[0].IsOptional.Should().BeTrue();
        builder.Steps[1].IsOptional.Should().BeFalse();
        builder.Steps[2].IsOptional.Should().BeTrue();
    }

    #endregion

    #region Optional with Other Modifiers Tests

    [Fact]
    public void OptionalStep_CanHaveTag()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Optional().Tag("optional-api");

        var step = builder.Steps[0];
        step.IsOptional.Should().BeTrue();
        step.Tag.Should().Be("optional-api");
    }

    [Fact]
    public void OptionalStep_CanHaveCondition()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Optional().OnlyWhen(s => true);

        var step = builder.Steps[0];
        step.IsOptional.Should().BeTrue();
        step.Condition.Should().NotBeNull();
    }

    [Fact]
    public void OptionalStep_CanHaveFailureAction()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Optional().IfFail().ContinueFlow();

        var step = builder.Steps[0];
        step.IsOptional.Should().BeTrue();
        step.FailureAction.Should().NotBeNull();
    }

    #endregion
}
