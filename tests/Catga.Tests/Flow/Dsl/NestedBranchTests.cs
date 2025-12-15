using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for nested branch structures in Flow DSL
/// </summary>
public class NestedBranchTests
{
    private class TestState : BaseFlowState
    {
        public int Value { get; set; }
        public override IEnumerable<string> GetChangedFieldNames() => Enumerable.Empty<string>();
    }

    private record TestCommand(string Id) : IMessage
    {
        public long MessageId => MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtLeastOnce;
    }

    #region Nested If Tests

    [Fact]
    public void NestedIf_InIfBranch_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 100)
            .If(s => s.Value > 500)
                .Send(s => new TestCommand("very-high"))
            .EndIf()
        .EndIf();

        builder.Steps[0].ThenBranch.Should().HaveCount(1);
        builder.Steps[0].ThenBranch[0].Type.Should().Be(StepType.If);
    }

    #endregion

    #region If with Switch Tests

    [Fact]
    public void IfWithSwitch_InThenBranch_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 0)
            .Switch(s => s.Value)
                .Case(1, c => c.Send(s => new TestCommand("one")))
                .Case(2, c => c.Send(s => new TestCommand("two")))
            .EndSwitch()
        .EndIf();

        builder.Steps[0].ThenBranch[0].Type.Should().Be(StepType.Switch);
    }

    #endregion

    #region Switch with If Tests

    [Fact]
    public void SwitchWithIf_InCase_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Value)
            .Case(1, c => c
                .If(s => s.Value > 0)
                    .Send(s => new TestCommand("positive"))
                .EndIf())
        .EndSwitch();

        builder.Steps[0].Cases[1][0].Type.Should().Be(StepType.If);
    }

    #endregion

    #region ForEach with If Tests

    [Fact]
    public void ForEachWithIf_InBody_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.ForEach(s => new[] { "a", "b" })
            .If(s => true)
                .Send((s, item) => new TestCommand(item))
            .EndIf()
        .EndForEach();

        builder.Steps[0].ForEachSteps[0].Type.Should().Be(StepType.If);
    }

    #endregion

    #region Complex Nesting Tests

    [Fact]
    public void ComplexNesting_IfSwitchForEach_Works()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 0)
            .Switch(s => s.Value)
                .Case(1, c => c
                    .ForEach(s => new[] { "x", "y" })
                        .Send((s, item) => new TestCommand(item))
                    .EndForEach())
            .EndSwitch()
        .EndIf();

        builder.Steps[0].ThenBranch[0].Cases[1][0].Type.Should().Be(StepType.ForEach);
    }

    #endregion
}
