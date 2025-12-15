using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep condition and predicate handling
/// </summary>
public class FlowStepConditionTests
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

    #region Condition Tests

    [Fact]
    public void Step_CanHaveCondition()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Send(s => new TestCommand("cmd")).OnlyWhen(s => true);

        builder.Steps[0].Condition.Should().NotBeNull();
    }

    [Fact]
    public void IfStep_HasBranchCondition()
    {
        var builder = new FlowBuilder<TestState>();
        builder.If(s => s.Value > 10)
            .Send(s => new TestCommand("1"))
        .EndIf();

        var ifStep = builder.Steps[0];
        ifStep.BranchCondition.Should().NotBeNull();
    }

    [Fact]
    public void SwitchStep_HasSwitchSelector()
    {
        var builder = new FlowBuilder<TestState>();
        builder.Switch(s => s.Value)
            .Case(1, c => c.Send(s => new TestCommand("1")))
        .EndSwitch();

        var switchStep = builder.Steps[0];
        switchStep.SwitchSelector.Should().NotBeNull();
    }

    #endregion

    #region Condition Evaluation Tests

    [Fact]
    public void Condition_CanEvaluateToTrue()
    {
        var state = new TestState { Value = 20 };
        Func<TestState, bool> condition = s => s.Value > 10;

        condition(state).Should().BeTrue();
    }

    [Fact]
    public void Condition_CanEvaluateToFalse()
    {
        var state = new TestState { Value = 5 };
        Func<TestState, bool> condition = s => s.Value > 10;

        condition(state).Should().BeFalse();
    }

    #endregion

    #region Multiple Conditions Tests

    [Fact]
    public void MultipleConditions_CanBeCombined()
    {
        var state = new TestState { Value = 15 };
        Func<TestState, bool> condition1 = s => s.Value > 10;
        Func<TestState, bool> condition2 = s => s.Value < 20;

        (condition1(state) && condition2(state)).Should().BeTrue();
    }

    #endregion
}
