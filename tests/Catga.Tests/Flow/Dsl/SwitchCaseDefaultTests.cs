using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for Switch/Case/Default branches in FlowBuilder
/// </summary>
public class SwitchCaseDefaultTests
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

    #region Basic Switch Tests

    [Fact]
    public void Switch_WithIntSelector_CreatesCorrectStructure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Status)
            .Case(1, c => c.Send(s => new TestCommand("one")))
            .Case(2, c => c.Send(s => new TestCommand("two")))
        .EndSwitch();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.Switch);
        builder.Steps[0].Cases.Should().HaveCount(2);
    }

    [Fact]
    public void Switch_WithStringSelector_CreatesCorrectStructure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Category)
            .Case("A", c => c.Send(s => new TestCommand("catA")))
            .Case("B", c => c.Send(s => new TestCommand("catB")))
        .EndSwitch();

        builder.Steps[0].Cases.Should().HaveCount(2);
    }

    #endregion

    #region Default Branch Tests

    [Fact]
    public void Switch_WithDefault_CreatesDefaultBranch()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Status)
            .Case(1, c => c.Send(s => new TestCommand("one")))
            .Default(c => c.Send(s => new TestCommand("default")))
        .EndSwitch();

        builder.Steps[0].DefaultBranch.Should().NotBeEmpty();
    }

    [Fact]
    public void Switch_CasesAndDefault_AllCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Status)
            .Case(1, c => c.Send(s => new TestCommand("one")))
            .Case(2, c => c.Send(s => new TestCommand("two")))
            .Case(3, c => c.Send(s => new TestCommand("three")))
            .Default(c => c.Send(s => new TestCommand("default")))
        .EndSwitch();

        builder.Steps[0].Cases.Should().HaveCount(3);
        builder.Steps[0].DefaultBranch.Should().NotBeEmpty();
    }

    #endregion

    #region Multiple Cases Tests

    [Fact]
    public void Switch_ManyCases_AllCreated()
    {
        var builder = new FlowBuilder<TestState>();
        var switchBuilder = builder.Switch(s => s.Status);

        for (int i = 1; i <= 10; i++)
        {
            var caseValue = i;
            switchBuilder.Case(caseValue, c => c.Send(s => new TestCommand($"case-{caseValue}")));
        }
        switchBuilder.EndSwitch();

        builder.Steps[0].Cases.Should().HaveCount(10);
    }

    #endregion

    #region Case with Multiple Steps Tests

    [Fact]
    public void Case_WithMultipleSteps_AllIncluded()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Status)
            .Case(1, c => c
                .Send(s => new TestCommand("step1"))
                .Send(s => new TestCommand("step2"))
                .Send(s => new TestCommand("step3")))
        .EndSwitch();

        builder.Steps[0].Cases[1].Should().HaveCount(3);
    }

    #endregion
}
