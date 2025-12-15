using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for StepBuilder chaining and fluent API
/// </summary>
public class StepBuilderChainTests
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

    #region Basic Chaining Tests

    [Fact]
    public void Send_ReturnsStepBuilder()
    {
        var builder = new FlowBuilder<TestState>();
        var result = builder.Send(s => new TestCommand("cmd"));

        result.Should().NotBeNull();
    }

    [Fact]
    public void ChainedSends_AllCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"))
            .Send(s => new TestCommand("3"));

        builder.Steps.Should().HaveCount(3);
    }

    #endregion

    #region Modifier Chaining Tests

    [Fact]
    public void Send_Tag_Optional_Chained()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("cmd"))
            .Tag("important")
            .Optional();

        var step = builder.Steps[0];
        step.Tag.Should().Be("important");
        step.IsOptional.Should().BeTrue();
    }

    [Fact]
    public void Send_OnlyWhen_IfFail_Chained()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("cmd"))
            .OnlyWhen(s => true)
            .IfFail().ContinueFlow();

        var step = builder.Steps[0];
        step.Condition.Should().NotBeNull();
        step.FailureAction.Should().NotBeNull();
    }

    #endregion

    #region Complex Chaining Tests

    [Fact]
    public void ComplexChain_MultipleModifiers_AllApplied()
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
