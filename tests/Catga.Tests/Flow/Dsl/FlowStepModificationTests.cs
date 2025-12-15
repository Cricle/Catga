using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep modification and mutation
/// </summary>
public class FlowStepModificationTests
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

    #region FlowStep Property Modification Tests

    [Fact]
    public void FlowStep_Type_CanBeSet()
    {
        var step = new FlowStep { Type = StepType.Send };
        step.Type = StepType.Query;

        step.Type.Should().Be(StepType.Query);
    }

    [Fact]
    public void FlowStep_Tag_CanBeSet()
    {
        var step = new FlowStep();
        step.Tag = "important";

        step.Tag.Should().Be("important");
    }

    [Fact]
    public void FlowStep_IsOptional_CanBeSet()
    {
        var step = new FlowStep();
        step.IsOptional = true;

        step.IsOptional.Should().BeTrue();
    }

    [Fact]
    public void FlowStep_Timeout_CanBeSet()
    {
        var step = new FlowStep();
        step.Timeout = TimeSpan.FromSeconds(30);

        step.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void FlowStep_Retry_CanBeSet()
    {
        var step = new FlowStep();
        step.Retry = 3;

        step.Retry.Should().Be(3);
    }

    #endregion

    #region FlowStep Collection Modification Tests

    [Fact]
    public void FlowStep_ThenBranch_CanBeModified()
    {
        var step = new FlowStep();
        var substep = new FlowStep { Type = StepType.Send };
        step.ThenBranch.Add(substep);

        step.ThenBranch.Should().Contain(substep);
    }

    [Fact]
    public void FlowStep_ForEachSteps_CanBeModified()
    {
        var step = new FlowStep();
        var substep = new FlowStep { Type = StepType.Send };
        step.ForEachSteps.Add(substep);

        step.ForEachSteps.Should().Contain(substep);
    }

    #endregion
}
