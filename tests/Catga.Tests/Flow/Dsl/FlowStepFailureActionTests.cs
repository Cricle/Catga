using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep failure action configuration
/// </summary>
public class FlowStepFailureActionTests
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

    #region Failure Action Tests

    [Fact]
    public void Step_CanHaveFailureAction()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).IfFail().ContinueFlow();

        builder.Steps[0].FailureAction.Should().NotBeNull();
    }

    [Fact]
    public void Step_CanContinueOnFailure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).IfFail().ContinueFlow();

        var step = builder.Steps[0];
        step.FailureAction.Should().NotBeNull();
    }

    [Fact]
    public void Step_CanStopOnFailure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).IfFail().StopFlow();

        var step = builder.Steps[0];
        step.FailureAction.Should().NotBeNull();
    }

    #endregion

    #region FailIf Condition Tests

    [Fact]
    public void Step_CanHaveFailIfCondition()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).FailIf(s => false).ContinueFlow();

        builder.Steps[0].FailureCondition.Should().NotBeNull();
    }

    #endregion

    #region Multiple Failure Actions Tests

    [Fact]
    public void MultipleSteps_CanHaveDifferentFailureActions()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1")).IfFail().ContinueFlow()
            .Send(s => new TestCommand("2")).IfFail().StopFlow();

        builder.Steps[0].FailureAction.Should().NotBeNull();
        builder.Steps[1].FailureAction.Should().NotBeNull();
    }

    #endregion
}
