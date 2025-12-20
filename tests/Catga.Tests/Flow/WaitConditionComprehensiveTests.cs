using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

/// <summary>
/// Comprehensive tests for WaitCondition, WaitType, FlowCompletedEventData
/// </summary>
public class WaitConditionComprehensiveTests
{
    #region WaitCondition Tests

    [Fact]
    public void WaitCondition_RequiredProperties_ShouldBeSet()
    {
        var condition = new WaitCondition
        {
            CorrelationId = "corr-123",
            Type = WaitType.All,
            ExpectedCount = 5,
            Timeout = TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-456",
            FlowType = "OrderFlow",
            Step = 3
        };

        condition.CorrelationId.Should().Be("corr-123");
        condition.Type.Should().Be(WaitType.All);
        condition.ExpectedCount.Should().Be(5);
        condition.Timeout.Should().Be(TimeSpan.FromMinutes(10));
        condition.FlowId.Should().Be("flow-456");
        condition.FlowType.Should().Be("OrderFlow");
        condition.Step.Should().Be(3);
    }

    [Fact]
    public void WaitCondition_DefaultValues_ShouldBeCorrect()
    {
        var condition = new WaitCondition
        {
            CorrelationId = "corr-123",
            Type = WaitType.All,
            ExpectedCount = 5,
            Timeout = TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-456",
            FlowType = "OrderFlow",
            Step = 3
        };

        condition.CompletedCount.Should().Be(0);
        condition.CancelOthers.Should().BeFalse();
        condition.ChildFlowIds.Should().BeEmpty();
        condition.Results.Should().BeEmpty();
        condition.ScheduleId.Should().BeNull();
    }

    [Fact]
    public void WaitCondition_CompletedCount_ShouldBeMutable()
    {
        var condition = new WaitCondition
        {
            CorrelationId = "corr-123",
            Type = WaitType.All,
            ExpectedCount = 5,
            Timeout = TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-456",
            FlowType = "OrderFlow",
            Step = 3
        };

        condition.CompletedCount = 3;
        condition.CompletedCount.Should().Be(3);
    }

    [Fact]
    public void WaitCondition_ChildFlowIds_ShouldBeMutable()
    {
        var condition = new WaitCondition
        {
            CorrelationId = "corr-123",
            Type = WaitType.All,
            ExpectedCount = 5,
            Timeout = TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-456",
            FlowType = "OrderFlow",
            Step = 3
        };

        condition.ChildFlowIds.Add("child-1");
        condition.ChildFlowIds.Add("child-2");

        condition.ChildFlowIds.Should().BeEquivalentTo(["child-1", "child-2"]);
    }

    [Fact]
    public void WaitCondition_Results_ShouldBeMutable()
    {
        var condition = new WaitCondition
        {
            CorrelationId = "corr-123",
            Type = WaitType.All,
            ExpectedCount = 5,
            Timeout = TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-456",
            FlowType = "OrderFlow",
            Step = 3
        };

        condition.Results.Add(new FlowCompletedEventData
        {
            FlowId = "child-1",
            Success = true
        });

        condition.Results.Should().HaveCount(1);
        condition.Results[0].FlowId.Should().Be("child-1");
    }

    [Fact]
    public void WaitCondition_WithCancelOthers_ShouldBeSet()
    {
        var condition = new WaitCondition
        {
            CorrelationId = "corr-123",
            Type = WaitType.Any,
            ExpectedCount = 3,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-456",
            FlowType = "OrderFlow",
            Step = 1,
            CancelOthers = true
        };

        condition.CancelOthers.Should().BeTrue();
    }

    [Fact]
    public void WaitCondition_WithScheduleId_ShouldBeSet()
    {
        var condition = new WaitCondition
        {
            CorrelationId = "corr-123",
            Type = WaitType.All,
            ExpectedCount = 2,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-456",
            FlowType = "OrderFlow",
            Step = 1,
            ScheduleId = "schedule-789"
        };

        condition.ScheduleId.Should().Be("schedule-789");
    }

    #endregion

    #region WaitType Tests

    [Fact]
    public void WaitType_All_ShouldBeCorrect()
    {
        WaitType.All.Should().Be(WaitType.All);
    }

    [Fact]
    public void WaitType_Any_ShouldBeCorrect()
    {
        WaitType.Any.Should().Be(WaitType.Any);
    }

    [Fact]
    public void WaitType_AllValues_ShouldBeDistinct()
    {
        var values = Enum.GetValues<WaitType>();
        values.Should().OnlyHaveUniqueItems();
        values.Should().HaveCount(2);
    }

    #endregion

    #region FlowCompletedEventData Tests

    [Fact]
    public void FlowCompletedEventData_RequiredProperties_ShouldBeSet()
    {
        var data = new FlowCompletedEventData
        {
            FlowId = "flow-123"
        };

        data.FlowId.Should().Be("flow-123");
    }

    [Fact]
    public void FlowCompletedEventData_DefaultValues_ShouldBeCorrect()
    {
        var data = new FlowCompletedEventData
        {
            FlowId = "flow-123"
        };

        data.ParentCorrelationId.Should().BeNull();
        data.Success.Should().BeFalse();
        data.Error.Should().BeNull();
        data.Result.Should().BeNull();
    }

    [Fact]
    public void FlowCompletedEventData_Success_ShouldBeSet()
    {
        var data = new FlowCompletedEventData
        {
            FlowId = "flow-123",
            Success = true,
            Result = "completed"
        };

        data.Success.Should().BeTrue();
        data.Result.Should().Be("completed");
    }

    [Fact]
    public void FlowCompletedEventData_Failure_ShouldBeSet()
    {
        var data = new FlowCompletedEventData
        {
            FlowId = "flow-123",
            Success = false,
            Error = "Something went wrong"
        };

        data.Success.Should().BeFalse();
        data.Error.Should().Be("Something went wrong");
    }

    [Fact]
    public void FlowCompletedEventData_WithParentCorrelationId_ShouldBeSet()
    {
        var data = new FlowCompletedEventData
        {
            FlowId = "flow-123",
            ParentCorrelationId = "parent-456",
            Success = true
        };

        data.ParentCorrelationId.Should().Be("parent-456");
    }

    [Fact]
    public void FlowCompletedEventData_WithComplexResult_ShouldBeSet()
    {
        var complexResult = new { OrderId = 123, Status = "Completed" };
        var data = new FlowCompletedEventData
        {
            FlowId = "flow-123",
            Success = true,
            Result = complexResult
        };

        data.Result.Should().BeEquivalentTo(complexResult);
    }

    #endregion
}
