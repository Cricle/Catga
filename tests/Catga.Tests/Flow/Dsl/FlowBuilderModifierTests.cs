using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowBuilder step modifiers
/// </summary>
public class FlowBuilderModifierTests
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

    #region Optional Modifier Tests

    [Fact]
    public void Optional_SetsIsOptional()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).Optional();

        builder.Steps[0].IsOptional.Should().BeTrue();
    }

    [Fact]
    public void Optional_DefaultIsFalse()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd"));

        builder.Steps[0].IsOptional.Should().BeFalse();
    }

    #endregion

    #region OnlyWhen Modifier Tests

    [Fact]
    public void OnlyWhen_SetsCondition()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).OnlyWhen(s => true);

        builder.Steps[0].Condition.Should().NotBeNull();
    }

    #endregion

    #region IfFail Modifier Tests

    [Fact]
    public void IfFail_ContinueFlow()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).IfFail().ContinueFlow();

        builder.Steps[0].FailureAction.Should().NotBeNull();
    }

    [Fact]
    public void IfFail_StopFlow()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).IfFail().StopFlow();

        builder.Steps[0].FailureAction.Should().NotBeNull();
    }

    [Fact]
    public void IfFail_Retry()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).IfFail().Retry(3);

        builder.Steps[0].FailureAction.Should().NotBeNull();
    }

    #endregion

    #region FailIf Modifier Tests

    [Fact]
    public void FailIf_SetsCondition()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).FailIf(s => false).ContinueFlow();

        builder.Steps[0].FailureCondition.Should().NotBeNull();
    }

    #endregion

    #region Combined Modifiers Tests

    [Fact]
    public void CombinedModifiers_AllApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("cmd"))
            .Tag("api")
            .Optional()
            .OnlyWhen(s => true)
            .IfFail().ContinueFlow();

        var step = builder.Steps[0];
        step.Tag.Should().Be("api");
        step.IsOptional.Should().BeTrue();
        step.Condition.Should().NotBeNull();
        step.FailureAction.Should().NotBeNull();
    }

    #endregion
}
