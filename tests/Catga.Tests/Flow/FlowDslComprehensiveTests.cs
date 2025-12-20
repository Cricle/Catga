using Catga.Abstractions;
using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

/// <summary>
/// Comprehensive tests for Flow DSL components
/// </summary>
public class FlowDslComprehensiveTests
{
    #region DslFlowResult Tests

    [Fact]
    public void DslFlowResult_Success_ShouldCreateSuccessResult()
    {
        var state = new TestFlowState { OrderId = "123" };
        
        var result = DslFlowResult<TestFlowState>.Success(state, DslFlowStatus.Completed, 5);
        
        result.IsSuccess.Should().BeTrue();
        result.State.Should().Be(state);
        result.Status.Should().Be(DslFlowStatus.Completed);
        result.CompletedSteps.Should().Be(5);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void DslFlowResult_Failure_ShouldCreateFailureResult()
    {
        var result = DslFlowResult<TestFlowState>.Failure(DslFlowStatus.Failed, "Test error", 3);
        
        result.IsSuccess.Should().BeFalse();
        result.State.Should().BeNull();
        result.Status.Should().Be(DslFlowStatus.Failed);
        result.CompletedSteps.Should().Be(3);
        result.Error.Should().Be("Test error");
    }

    [Fact]
    public void DslFlowResult_FailureWithState_ShouldIncludeState()
    {
        var state = new TestFlowState { OrderId = "456" };
        
        var result = DslFlowResult<TestFlowState>.Failure(state, DslFlowStatus.Failed, "Error with state", 2);
        
        result.IsSuccess.Should().BeFalse();
        result.State.Should().Be(state);
        result.Status.Should().Be(DslFlowStatus.Failed);
        result.Error.Should().Be("Error with state");
    }

    [Fact]
    public void DslFlowResult_Suspended_ShouldCreateSuspendedResult()
    {
        var state = new TestFlowState { OrderId = "789" };
        
        var result = DslFlowResult<TestFlowState>.Suspended(state, 4, "schedule-123");
        
        result.IsSuccess.Should().BeTrue();
        result.State.Should().Be(state);
        result.Status.Should().Be(DslFlowStatus.Suspended);
        result.CompletedSteps.Should().Be(4);
        result.ScheduleId.Should().Be("schedule-123");
    }

    [Fact]
    public void DslFlowResult_WithFlowId_ShouldSetFlowId()
    {
        var result = new DslFlowResult<TestFlowState>(true, null, DslFlowStatus.Completed, 0)
        {
            FlowId = "flow-abc"
        };
        
        result.FlowId.Should().Be("flow-abc");
    }

    #endregion

    #region DslFlowOptions Tests

    [Fact]
    public void DslFlowOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new DslFlowOptions();
        
        options.KeyPrefix.Should().Be("flow");
        options.DefaultTimeout.Should().Be(TimeSpan.FromMinutes(10));
        options.DefaultRetries.Should().Be(0);
        options.EnableTracing.Should().BeTrue();
        options.EnableMetrics.Should().BeTrue();
        options.NodeId.Should().BeNull();
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.ClaimTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void DslFlowOptions_CustomValues_ShouldBeSet()
    {
        var options = new DslFlowOptions
        {
            KeyPrefix = "custom-flow",
            DefaultTimeout = TimeSpan.FromMinutes(30),
            DefaultRetries = 3,
            EnableTracing = false,
            EnableMetrics = false,
            NodeId = "node-1",
            HeartbeatInterval = TimeSpan.FromSeconds(10),
            ClaimTimeout = TimeSpan.FromMinutes(1)
        };
        
        options.KeyPrefix.Should().Be("custom-flow");
        options.DefaultTimeout.Should().Be(TimeSpan.FromMinutes(30));
        options.DefaultRetries.Should().Be(3);
        options.EnableTracing.Should().BeFalse();
        options.EnableMetrics.Should().BeFalse();
        options.NodeId.Should().Be("node-1");
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(10));
        options.ClaimTimeout.Should().Be(TimeSpan.FromMinutes(1));
    }

    #endregion

    #region DslFlowStatus Tests

    [Theory]
    [InlineData(DslFlowStatus.NotStarted)]
    [InlineData(DslFlowStatus.Running)]
    [InlineData(DslFlowStatus.Completed)]
    [InlineData(DslFlowStatus.Failed)]
    [InlineData(DslFlowStatus.Suspended)]
    [InlineData(DslFlowStatus.Cancelled)]
    public void DslFlowStatus_AllValues_ShouldBeDefined(DslFlowStatus status)
    {
        Enum.IsDefined(typeof(DslFlowStatus), status).Should().BeTrue();
    }

    #endregion

    #region FlowStep Tests

    [Fact]
    public void FlowStep_DefaultValues_ShouldBeCorrect()
    {
        var step = new FlowStep();
        
        step.Type.Should().Be(StepType.Send);
        step.HasResult.Should().BeFalse();
        step.ResultPropertyName.Should().BeNull();
        step.HasCompensation.Should().BeFalse();
        step.HasCondition.Should().BeFalse();
        step.HasFailCondition.Should().BeFalse();
        step.IsOptional.Should().BeFalse();
        step.Tags.Should().BeEmpty();
        step.Timeout.Should().BeNull();
        step.ChildRequestCount.Should().Be(0);
        step.HasOnCompletedHook.Should().BeFalse();
        step.HasOnFailedHook.Should().BeFalse();
        step.BatchSize.Should().Be(100);
        step.FailureHandling.Should().Be(ForEachFailureHandling.StopOnFirstFailure);
    }

    [Fact]
    public void FlowStep_CustomValues_ShouldBeSet()
    {
        var step = new FlowStep
        {
            Type = StepType.Query,
            HasResult = true,
            ResultPropertyName = "Result",
            HasCompensation = true,
            HasCondition = true,
            HasFailCondition = true,
            IsOptional = true,
            Timeout = TimeSpan.FromSeconds(30),
            ChildRequestCount = 5,
            HasOnCompletedHook = true,
            HasOnFailedHook = true,
            BatchSize = 50,
            FailureHandling = ForEachFailureHandling.ContinueOnFailure
        };
        step.Tags.Add("important");
        
        step.Type.Should().Be(StepType.Query);
        step.HasResult.Should().BeTrue();
        step.ResultPropertyName.Should().Be("Result");
        step.HasCompensation.Should().BeTrue();
        step.HasCondition.Should().BeTrue();
        step.HasFailCondition.Should().BeTrue();
        step.IsOptional.Should().BeTrue();
        step.Tags.Should().Contain("important");
        step.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        step.ChildRequestCount.Should().Be(5);
        step.HasOnCompletedHook.Should().BeTrue();
        step.HasOnFailedHook.Should().BeTrue();
        step.BatchSize.Should().Be(50);
        step.FailureHandling.Should().Be(ForEachFailureHandling.ContinueOnFailure);
    }

    [Fact]
    public void FlowStep_BranchingProperties_ShouldBeSettable()
    {
        var step = new FlowStep
        {
            Type = StepType.If,
            ThenBranch = [new FlowStep { Type = StepType.Send }],
            ElseBranch = [new FlowStep { Type = StepType.Query }]
        };
        
        step.ThenBranch.Should().HaveCount(1);
        step.ThenBranch![0].Type.Should().Be(StepType.Send);
        step.ElseBranch.Should().HaveCount(1);
        step.ElseBranch![0].Type.Should().Be(StepType.Query);
    }

    [Fact]
    public void FlowStep_SwitchProperties_ShouldBeSettable()
    {
        var step = new FlowStep
        {
            Type = StepType.Switch,
            Cases = new Dictionary<object, List<FlowStep>>
            {
                ["case1"] = [new FlowStep { Type = StepType.Send }],
                ["case2"] = [new FlowStep { Type = StepType.Query }]
            },
            DefaultBranch = [new FlowStep { Type = StepType.Publish }]
        };
        
        step.Cases.Should().HaveCount(2);
        step.Cases!["case1"].Should().HaveCount(1);
        step.DefaultBranch.Should().HaveCount(1);
    }

    [Fact]
    public void FlowStep_DelayProperties_ShouldBeSettable()
    {
        var step = new FlowStep
        {
            Type = StepType.Delay,
            DelayDuration = TimeSpan.FromMinutes(5)
        };
        
        step.DelayDuration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void FlowStep_ForEachProperties_ShouldBeSettable()
    {
        var step = new FlowStep
        {
            Type = StepType.ForEach,
            ItemSteps = [new FlowStep { Type = StepType.Send }],
            BatchSize = 25
        };
        
        step.ItemSteps.Should().HaveCount(1);
        step.BatchSize.Should().Be(25);
    }

    #endregion

    #region FlowStep<TState> Tests

    [Fact]
    public void FlowStepGeneric_DefaultValues_ShouldBeCorrect()
    {
        var step = new FlowStep<TestFlowState>();
        
        step.Type.Should().Be(StepType.Send);
        step.HasResult.Should().BeFalse();
        step.Tags.Should().BeEmpty();
        step.BatchSize.Should().Be(100);
        step.FailureHandling.Should().Be(ForEachFailureHandling.StopOnFirstFailure);
    }

    [Fact]
    public void FlowStepGeneric_WithDelegates_ShouldBeSettable()
    {
        var step = new FlowStep<TestFlowState>
        {
            Type = StepType.Query,
            HasResult = true,
            ResultPropertyName = "OrderId"
        };
        
        step.Type.Should().Be(StepType.Query);
        step.HasResult.Should().BeTrue();
        step.ResultPropertyName.Should().Be("OrderId");
    }

    #endregion

    #region StepType Tests

    [Theory]
    [InlineData(StepType.Send, 0)]
    [InlineData(StepType.Query, 1)]
    [InlineData(StepType.Publish, 2)]
    [InlineData(StepType.WhenAll, 3)]
    [InlineData(StepType.WhenAny, 4)]
    [InlineData(StepType.If, 5)]
    [InlineData(StepType.Switch, 6)]
    [InlineData(StepType.ForEach, 7)]
    [InlineData(StepType.Delay, 8)]
    [InlineData(StepType.ScheduleAt, 9)]
    public void StepType_AllValues_ShouldHaveCorrectValue(StepType type, int expectedValue)
    {
        ((int)type).Should().Be(expectedValue);
    }

    #endregion

    #region ForEachFailureHandling Tests

    [Theory]
    [InlineData(ForEachFailureHandling.StopOnFirstFailure)]
    [InlineData(ForEachFailureHandling.ContinueOnFailure)]
    [InlineData(ForEachFailureHandling.CollectErrors)]
    public void ForEachFailureHandling_AllValues_ShouldBeDefined(ForEachFailureHandling handling)
    {
        Enum.IsDefined(typeof(ForEachFailureHandling), handling).Should().BeTrue();
    }

    #endregion

    #region ForEachMetrics Tests

    [Fact]
    public void ForEachMetrics_DefaultValues_ShouldBeZero()
    {
        var metrics = new ForEachMetrics();
        
        metrics.TotalItems.Should().Be(0);
        metrics.ProcessedItems.Should().Be(0);
        metrics.SuccessfulItems.Should().Be(0);
        metrics.FailedItems.Should().Be(0);
        metrics.SkippedItems.Should().Be(0);
    }

    [Fact]
    public void ForEachMetrics_CustomValues_ShouldBeSet()
    {
        var metrics = new ForEachMetrics
        {
            TotalItems = 100,
            ProcessedItems = 80,
            SuccessfulItems = 75,
            FailedItems = 5,
            SkippedItems = 20
        };
        
        metrics.TotalItems.Should().Be(100);
        metrics.ProcessedItems.Should().Be(80);
        metrics.SuccessfulItems.Should().Be(75);
        metrics.FailedItems.Should().Be(5);
        metrics.SkippedItems.Should().Be(20);
    }

    #endregion

    #region Test Helpers

    public class TestFlowState : IFlowState
    {
        public string? OrderId { get; set; }
        public string? CustomerId { get; set; }
        public decimal Amount { get; set; }
    }

    #endregion
}
