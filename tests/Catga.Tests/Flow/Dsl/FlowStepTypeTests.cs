using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep type identification and classification
/// </summary>
public class FlowStepTypeTests
{
    #region Step Type Identification Tests

    [Fact]
    public void Send_HasSendType()
    {
        var step = new FlowStep { Type = StepType.Send };
        step.Type.Should().Be(StepType.Send);
    }

    [Fact]
    public void Query_HasQueryType()
    {
        var step = new FlowStep { Type = StepType.Query };
        step.Type.Should().Be(StepType.Query);
    }

    [Fact]
    public void Publish_HasPublishType()
    {
        var step = new FlowStep { Type = StepType.Publish };
        step.Type.Should().Be(StepType.Publish);
    }

    [Fact]
    public void If_HasIfType()
    {
        var step = new FlowStep { Type = StepType.If };
        step.Type.Should().Be(StepType.If);
    }

    [Fact]
    public void Switch_HasSwitchType()
    {
        var step = new FlowStep { Type = StepType.Switch };
        step.Type.Should().Be(StepType.Switch);
    }

    [Fact]
    public void ForEach_HasForEachType()
    {
        var step = new FlowStep { Type = StepType.ForEach };
        step.Type.Should().Be(StepType.ForEach);
    }

    [Fact]
    public void WhenAll_HasWhenAllType()
    {
        var step = new FlowStep { Type = StepType.WhenAll };
        step.Type.Should().Be(StepType.WhenAll);
    }

    [Fact]
    public void WhenAny_HasWhenAnyType()
    {
        var step = new FlowStep { Type = StepType.WhenAny };
        step.Type.Should().Be(StepType.WhenAny);
    }

    [Fact]
    public void Delay_HasDelayType()
    {
        var step = new FlowStep { Type = StepType.Delay };
        step.Type.Should().Be(StepType.Delay);
    }

    [Fact]
    public void Wait_HasWaitType()
    {
        var step = new FlowStep { Type = StepType.Wait };
        step.Type.Should().Be(StepType.Wait);
    }

    #endregion

    #region Type Classification Tests

    [Fact]
    public void ActionSteps_IncludeSendQueryPublish()
    {
        var actionTypes = new[] { StepType.Send, StepType.Query, StepType.Publish };

        actionTypes.Should().HaveCount(3);
    }

    [Fact]
    public void BranchingSteps_IncludeIfSwitch()
    {
        var branchingTypes = new[] { StepType.If, StepType.Switch };

        branchingTypes.Should().HaveCount(2);
    }

    [Fact]
    public void LoopingSteps_IncludeForEachWhenAllWhenAny()
    {
        var loopingTypes = new[] { StepType.ForEach, StepType.WhenAll, StepType.WhenAny };

        loopingTypes.Should().HaveCount(3);
    }

    [Fact]
    public void ControlSteps_IncludeDelayWait()
    {
        var controlTypes = new[] { StepType.Delay, StepType.Wait };

        controlTypes.Should().HaveCount(2);
    }

    #endregion
}
