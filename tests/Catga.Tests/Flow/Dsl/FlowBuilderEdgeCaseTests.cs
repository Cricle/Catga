using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Edge case tests for FlowBuilder - testing boundary conditions and unusual scenarios
/// </summary>
public class FlowBuilderEdgeCaseTests
{
    private class TestState : BaseFlowState
    {
        public string Data { get; set; } = "";
        public int Counter { get; set; }
        public List<string> Items { get; set; } = new();
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    private record TestEvent(string Type) : IEvent
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Empty Flow Tests

    [Fact]
    public void EmptyFlow_HasNoSteps()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Steps.Should().BeEmpty();
    }

    [Fact]
    public void EmptyFlow_CanBuildWithoutError()
    {
        var builder = new FlowBuilder<TestState>();
        var act = () => builder.Steps.ToList();
        act.Should().NotThrow();
    }

    #endregion

    #region Single Step Tests

    [Fact]
    public void SingleSend_CreatesOneStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("single"));

        builder.Steps.Should().HaveCount(1);
    }

    [Fact]
    public void SinglePublish_CreatesOneStep()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Publish(s => new TestEvent("single"));

        builder.Steps.Should().HaveCount(1);
    }

    #endregion

    #region Large Flow Tests

    [Fact]
    public void LargeFlow_ManySteps_AllAdded()
    {
        var builder = new FlowBuilder<TestState>();

        for (int i = 0; i < 100; i++)
        {
            builder.Send(s => new TestCommand($"step-{i}"));
        }

        builder.Steps.Should().HaveCount(100);
    }

    [Fact]
    public void LargeFlow_MixedStepTypes_AllAdded()
    {
        var builder = new FlowBuilder<TestState>();

        for (int i = 0; i < 50; i++)
        {
            builder.Send(s => new TestCommand($"send-{i}"));
            builder.Publish(s => new TestEvent($"publish-{i}"));
        }

        builder.Steps.Should().HaveCount(100);
    }

    #endregion

    #region Nested Structure Tests

    [Fact]
    public void DeeplyNestedIf_CreatesCorrectStructure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Counter > 0)
            .If(s => s.Counter > 10)
                .If(s => s.Counter > 100)
                    .Send(s => new TestCommand("deep"))
                .EndIf()
            .EndIf()
        .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].ThenBranch.Should().HaveCount(1);
    }

    [Fact]
    public void NestedSwitchInIf_CreatesCorrectStructure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Counter > 0)
            .Switch(s => s.Counter)
                .Case(1, c => c.Send(s => new TestCommand("case1")))
                .Case(2, c => c.Send(s => new TestCommand("case2")))
            .EndSwitch()
        .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].ThenBranch.Should().HaveCount(1);
        builder.Steps[0].ThenBranch[0].Type.Should().Be(StepType.Switch);
    }

    [Fact]
    public void NestedForEachInIf_CreatesCorrectStructure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Items.Any())
            .ForEach(s => s.Items)
                .Send((s, item) => new TestCommand(item))
            .EndForEach()
        .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].ThenBranch.Should().HaveCount(1);
        builder.Steps[0].ThenBranch[0].Type.Should().Be(StepType.ForEach);
    }

    #endregion

    #region Chaining Tests

    [Fact]
    public void FluentChaining_AllStepsInOrder()
    {
        var builder = new FlowBuilder<TestState>();
        builder
            .Send(s => new TestCommand("1"))
            .Delay(TimeSpan.FromSeconds(1))
            .Send(s => new TestCommand("2"))
            .Publish(s => new TestEvent("3"))
            .Send(s => new TestCommand("4"));

        builder.Steps.Should().HaveCount(5);
        builder.Steps[0].Type.Should().Be(StepType.Send);
        builder.Steps[1].Type.Should().Be(StepType.Delay);
        builder.Steps[2].Type.Should().Be(StepType.Send);
        builder.Steps[3].Type.Should().Be(StepType.Publish);
        builder.Steps[4].Type.Should().Be(StepType.Send);
    }

    [Fact]
    public void ChainedModifiers_AllApplied()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("modified"))
            .Tag("tagged")
            .Optional()
            .OnlyWhen(s => s.Counter > 0);

        builder.Steps[0].Tag.Should().Be("tagged");
        builder.Steps[0].IsOptional.Should().BeTrue();
        builder.Steps[0].Condition.Should().NotBeNull();
    }

    #endregion

    #region Name and Configuration Tests

    [Fact]
    public void FlowName_CanBeSet()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("MyTestFlow");

        builder.FlowName.Should().Be("MyTestFlow");
    }

    [Fact]
    public void FlowName_EmptyString_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("");

        builder.FlowName.Should().BeEmpty();
    }

    [Fact]
    public void FlowName_SpecialCharacters_IsValid()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Name("Flow-名前_テスト.v1");

        builder.FlowName.Should().Be("Flow-名前_テスト.v1");
    }

    #endregion
}
