using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

public class FlowStepTests
{
    [Fact]
    public void FlowStep_CanBeCreated()
    {
        var step = new FlowStep();
        step.Should().NotBeNull();
    }

    [Fact]
    public void FlowStep_Type_CanBeSet()
    {
        var step = new FlowStep { Type = StepType.Send };
        step.Type.Should().Be(StepType.Send);
    }

    [Fact]
    public void FlowStep_Tag_CanBeSet()
    {
        var step = new FlowStep { Tag = "important" };
        step.Tag.Should().Be("important");
    }

    [Fact]
    public void FlowStep_ThenBranch_CanBeSet()
    {
        var step = new FlowStep
        {
            Type = StepType.If,
            ThenBranch = new List<FlowStep>
            {
                new() { Type = StepType.Send }
            }
        };

        step.ThenBranch.Should().HaveCount(1);
    }

    [Fact]
    public void FlowStep_ElseBranch_CanBeSet()
    {
        var step = new FlowStep
        {
            Type = StepType.If,
            ElseBranch = new List<FlowStep>
            {
                new() { Type = StepType.Publish }
            }
        };

        step.ElseBranch.Should().HaveCount(1);
    }

    [Fact]
    public void FlowStep_ForEachStep_Properties()
    {
        var step = new FlowStep
        {
            Type = StepType.ForEach,
            MaxParallelism = 4,
            BatchSize = 10
        };

        step.Type.Should().Be(StepType.ForEach);
        step.MaxParallelism.Should().Be(4);
        step.BatchSize.Should().Be(10);
    }

    [Theory]
    [InlineData(StepType.Send)]
    [InlineData(StepType.Query)]
    [InlineData(StepType.Publish)]
    [InlineData(StepType.If)]
    [InlineData(StepType.Switch)]
    [InlineData(StepType.ForEach)]
    public void FlowStep_AllStepTypes_CanBeUsed(StepType stepType)
    {
        var step = new FlowStep { Type = stepType };
        step.Type.Should().Be(stepType);
    }
}
