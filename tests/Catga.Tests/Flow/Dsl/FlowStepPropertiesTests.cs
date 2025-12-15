using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Dsl;

/// <summary>
/// Tests for FlowStep properties
/// </summary>
public class FlowStepPropertiesTests
{
    #region Basic Properties Tests

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
    public void FlowStep_IsOptional_DefaultFalse()
    {
        var step = new FlowStep();
        step.IsOptional.Should().BeFalse();
    }

    [Fact]
    public void FlowStep_IsOptional_CanBeTrue()
    {
        var step = new FlowStep { IsOptional = true };
        step.IsOptional.Should().BeTrue();
    }

    #endregion

    #region Timeout and Retry Tests

    [Fact]
    public void FlowStep_Timeout_CanBeSet()
    {
        var step = new FlowStep { Timeout = TimeSpan.FromSeconds(30) };
        step.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void FlowStep_MaxRetries_CanBeSet()
    {
        var step = new FlowStep { MaxRetries = 5 };
        step.MaxRetries.Should().Be(5);
    }

    #endregion

    #region ForEach Properties Tests

    [Fact]
    public void FlowStep_MaxParallelism_CanBeSet()
    {
        var step = new FlowStep { MaxParallelism = 8 };
        step.MaxParallelism.Should().Be(8);
    }

    [Fact]
    public void FlowStep_BatchSize_CanBeSet()
    {
        var step = new FlowStep { BatchSize = 100 };
        step.BatchSize.Should().Be(100);
    }

    [Fact]
    public void FlowStep_ContinueOnFailure_CanBeSet()
    {
        var step = new FlowStep { ContinueOnFailure = true };
        step.ContinueOnFailure.Should().BeTrue();
    }

    #endregion

    #region Delay Properties Tests

    [Fact]
    public void FlowStep_DelayDuration_CanBeSet()
    {
        var step = new FlowStep { DelayDuration = TimeSpan.FromMinutes(5) };
        step.DelayDuration.Should().Be(TimeSpan.FromMinutes(5));
    }

    #endregion

    #region Branch Properties Tests

    [Fact]
    public void FlowStep_ThenBranch_CanBeSet()
    {
        var step = new FlowStep
        {
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
            ElseBranch = new List<FlowStep>
            {
                new() { Type = StepType.Publish }
            }
        };

        step.ElseBranch.Should().HaveCount(1);
    }

    [Fact]
    public void FlowStep_Cases_CanBeSet()
    {
        var step = new FlowStep
        {
            Cases = new Dictionary<object, List<FlowStep>>
            {
                [1] = new() { new FlowStep { Type = StepType.Send } },
                [2] = new() { new FlowStep { Type = StepType.Publish } }
            }
        };

        step.Cases.Should().HaveCount(2);
    }

    [Fact]
    public void FlowStep_DefaultBranch_CanBeSet()
    {
        var step = new FlowStep
        {
            DefaultBranch = new List<FlowStep>
            {
                new() { Type = StepType.Send }
            }
        };

        step.DefaultBranch.Should().HaveCount(1);
    }

    [Fact]
    public void FlowStep_ParallelBranches_CanBeSet()
    {
        var step = new FlowStep
        {
            ParallelBranches = new List<List<FlowStep>>
            {
                new() { new FlowStep { Type = StepType.Send } },
                new() { new FlowStep { Type = StepType.Query } }
            }
        };

        step.ParallelBranches.Should().HaveCount(2);
    }

    #endregion

    #region Concurrent Tests

    [Fact]
    public void FlowStep_ConcurrentCreation_AllValid()
    {
        var steps = new System.Collections.Concurrent.ConcurrentBag<FlowStep>();

        Parallel.For(0, 100, i =>
        {
            steps.Add(new FlowStep
            {
                Type = (StepType)(i % 10),
                Tag = $"tag-{i}",
                MaxRetries = i
            });
        });

        steps.Count.Should().Be(100);
        steps.Select(s => s.Tag).Distinct().Count().Should().Be(100);
    }

    #endregion
}
