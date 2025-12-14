using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

public class SwitchBuilderTests
{
    private class TestState : BaseFlowState
    {
        public int Status { get; set; }
        public string Category { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    [Fact]
    public void Switch_CreatesSwitchStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Status)
            .Case(1, c => c.Send(s => new TestCommand("case1")))
            .Case(2, c => c.Send(s => new TestCommand("case2")))
            .EndSwitch();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Switch);
    }

    [Fact]
    public void Switch_WithDefault_CreatesDefaultBranch()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Status)
            .Case(1, c => c.Send(s => new TestCommand("case1")))
            .Default(c => c.Send(s => new TestCommand("default")))
            .EndSwitch();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].DefaultBranch.Should().NotBeEmpty();
    }

    [Fact]
    public void Switch_WithStringSelector_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Category)
            .Case("A", c => c.Send(s => new TestCommand("categoryA")))
            .Case("B", c => c.Send(s => new TestCommand("categoryB")))
            .EndSwitch();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Switch);
    }

    [Fact]
    public void Switch_MultipleCases_AllRegistered()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Status)
            .Case(1, c => c.Send(s => new TestCommand("case1")))
            .Case(2, c => c.Send(s => new TestCommand("case2")))
            .Case(3, c => c.Send(s => new TestCommand("case3")))
            .Case(4, c => c.Send(s => new TestCommand("case4")))
            .EndSwitch();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Cases.Should().HaveCount(4);
    }
}
