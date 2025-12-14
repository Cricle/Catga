using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

public class StepBuilderTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public int Counter { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    [Fact]
    public void Send_WithTag_SetsTag()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand(s.Data)).Tag("important");

        builder.Steps[0].Tag.Should().Be("important");
    }

    [Fact]
    public void Send_ChainedSteps_AddsMultipleSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("step1"))
            .Send(s => new TestCommand("step2"))
            .Send(s => new TestCommand("step3"));

        builder.Steps.Should().HaveCount(3);
    }

    [Fact]
    public void Send_WithOnlyWhen_SetsCondition()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand(s.Data))
            .OnlyWhen(s => s.Counter > 0);

        builder.Steps[0].Condition.Should().NotBeNull();
    }

    [Fact]
    public void Send_WithOptional_SetsOptionalFlag()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand(s.Data)).Optional();

        builder.Steps[0].IsOptional.Should().BeTrue();
    }

    [Fact]
    public void Send_WithIfFail_SetsFailureHandler()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand(s.Data))
            .IfFail().ContinueFlow();

        builder.Steps[0].FailureAction.Should().NotBeNull();
    }

    [Fact]
    public void Send_MultipleModifiers_AllApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand(s.Data))
            .Tag("tagged")
            .Optional()
            .OnlyWhen(s => s.Counter > 0);

        builder.Steps[0].Tag.Should().Be("tagged");
        builder.Steps[0].IsOptional.Should().BeTrue();
        builder.Steps[0].Condition.Should().NotBeNull();
    }
}
