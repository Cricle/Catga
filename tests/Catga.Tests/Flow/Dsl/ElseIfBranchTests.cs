using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for ElseIf branches in FlowBuilder
/// </summary>
public class ElseIfBranchTests
{
    private class TestState : BaseFlowState
    {
        public int Value { get; set; }
        public string Category { get; set; } = "";
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Single ElseIf Tests

    [Fact]
    public void IfElseIf_CreatesCorrectStructure()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 100)
            .Send(s => new TestCommand("high"))
        .ElseIf(s => s.Value > 50)
            .Send(s => new TestCommand("medium"))
        .EndIf();

        builder.Steps.Should().HaveCount(1);
        builder.Steps[0].Type.Should().Be(StepType.If);
        builder.Steps[0].ElseIfBranches.Should().HaveCount(1);
    }

    #endregion

    #region Multiple ElseIf Tests

    [Fact]
    public void MultipleElseIf_AllBranchesCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 1000)
            .Send(s => new TestCommand("huge"))
        .ElseIf(s => s.Value > 500)
            .Send(s => new TestCommand("large"))
        .ElseIf(s => s.Value > 100)
            .Send(s => new TestCommand("medium"))
        .ElseIf(s => s.Value > 50)
            .Send(s => new TestCommand("small"))
        .EndIf();

        builder.Steps[0].ElseIfBranches.Should().HaveCount(3);
    }

    #endregion

    #region ElseIf with Else Tests

    [Fact]
    public void IfElseIfElse_AllBranchesCreated()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 100)
            .Send(s => new TestCommand("high"))
        .ElseIf(s => s.Value > 50)
            .Send(s => new TestCommand("medium"))
        .Else()
            .Send(s => new TestCommand("low"))
        .EndIf();

        builder.Steps[0].ThenBranch.Should().NotBeEmpty();
        builder.Steps[0].ElseIfBranches.Should().HaveCount(1);
        builder.Steps[0].ElseBranch.Should().NotBeEmpty();
    }

    #endregion

    #region ElseIf with Multiple Steps Tests

    [Fact]
    public void ElseIf_WithMultipleSteps_AllIncluded()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 100)
            .Send(s => new TestCommand("1"))
            .Send(s => new TestCommand("2"))
        .ElseIf(s => s.Value > 50)
            .Send(s => new TestCommand("3"))
            .Send(s => new TestCommand("4"))
            .Send(s => new TestCommand("5"))
        .EndIf();

        builder.Steps[0].ThenBranch.Should().HaveCount(2);
        builder.Steps[0].ElseIfBranches[0].Steps.Should().HaveCount(3);
    }

    #endregion
}
