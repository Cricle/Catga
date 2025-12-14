using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

public class FlowBuilderApiTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public int Counter { get; set; }
        public bool Flag { get; set; }

        public override IEnumerable<string> GetChangedFieldNames()
        {
            yield break;
        }
    }

    private record TestCommand(string Id) : ICommand
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record TestEvent(string Id) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    [Fact]
    public void FlowBuilder_CanBeCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Should().NotBeNull();
    }

    [Fact]
    public void FlowBuilder_Steps_InitiallyEmpty()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Steps.Should().BeEmpty();
    }

    [Fact]
    public void FlowBuilder_Name_CanBeSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("TestFlow");
        builder.FlowName.Should().Be("TestFlow");
    }

    [Fact]
    public void FlowBuilder_Send_AddsStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand(s.Data));
        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Send);
    }

    [Fact]
    public void FlowBuilder_Publish_AddsStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Publish(s => new TestEvent(s.Data));
        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Publish);
    }

    [Fact]
    public void FlowBuilder_If_AddsConditionalStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Flag)
            .Send(s => new TestCommand("if-true"))
            .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.If);
    }

    [Fact]
    public void FlowBuilder_Switch_AddsSwitchStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Counter)
            .Case(1, c => c.Send(s => new TestCommand("case-1")))
            .Case(2, c => c.Send(s => new TestCommand("case-2")))
            .EndSwitch();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Switch);
    }

    [Fact]
    public void FlowBuilder_ForEach_AddsForEachStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => new[] { 1, 2, 3 })
            .Send((s, item) => new TestCommand($"item-{item}"))
            .EndForEach();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.ForEach);
    }

    [Fact]
    public void FlowBuilder_MultipleSteps_AddsAllSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("step1"))
            .Publish(s => new TestEvent("step2"))
            .Send(s => new TestCommand("step3"));

        builder.Steps.Should().HaveCount(3);
    }
}
