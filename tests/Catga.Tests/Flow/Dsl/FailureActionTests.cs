using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FailureAction configuration in FlowBuilder
/// </summary>
public class FailureActionTests
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

    #region IfFail Configuration Tests

    [Fact]
    public void IfFail_ContinueFlow_SetsFailureAction()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .IfFail().ContinueFlow();

        builder.Steps[0].FailureAction.Should().NotBeNull();
    }

    [Fact]
    public void IfFail_StopFlow_SetsFailureAction()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .IfFail().StopFlow();

        builder.Steps[0].FailureAction.Should().NotBeNull();
    }

    [Fact]
    public void IfFail_Retry_SetsFailureAction()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .IfFail().Retry(3);

        builder.Steps[0].FailureAction.Should().NotBeNull();
    }

    #endregion

    #region FailIf Configuration Tests

    [Fact]
    public void FailIf_WithCondition_SetsFailCondition()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .FailIf(s => true, "Custom error");

        builder.Steps[0].FailCondition.Should().NotBeNull();
    }

    #endregion

    #region Combined Tests

    [Fact]
    public void Step_WithMultipleModifiers_AllApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"))
            .Tag("important")
            .Optional()
            .IfFail().ContinueFlow();

        var step = builder.Steps[0];
        step.Tag.Should().Be("important");
        step.IsOptional.Should().BeTrue();
        step.FailureAction.Should().NotBeNull();
    }

    #endregion
}
