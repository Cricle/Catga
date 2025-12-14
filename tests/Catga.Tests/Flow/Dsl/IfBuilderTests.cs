using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

public class IfBuilderTests
{
    private class TestState : BaseFlowState
    {
        public bool Condition { get; set; }
        public int Value { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    [Fact]
    public void If_CreatesIfStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Condition)
            .Send(s => new TestCommand("then"))
            .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.If);
    }

    [Fact]
    public void If_WithElse_CreatesBothBranches()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Condition)
            .Send(s => new TestCommand("then"))
            .Else()
            .Send(s => new TestCommand("else"))
            .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.If);
        builder.Steps[0].ThenBranch.Should().NotBeEmpty();
        builder.Steps[0].ElseBranch.Should().NotBeEmpty();
    }

    [Fact]
    public void If_WithElseIf_CreatesElseIfBranch()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 100)
            .Send(s => new TestCommand("high"))
            .ElseIf(s => s.Value > 50)
            .Send(s => new TestCommand("medium"))
            .Else()
            .Send(s => new TestCommand("low"))
            .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.If);
    }

    [Fact]
    public void NestedIf_CreatesNestedStructure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Condition)
            .If(s => s.Value > 50)
                .Send(s => new TestCommand("nested"))
            .EndIf()
        .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].ThenBranch.Should().HaveCount(1);
        builder.Steps[0].ThenBranch[0].Type.Should().Be(StepType.If);
    }
}
