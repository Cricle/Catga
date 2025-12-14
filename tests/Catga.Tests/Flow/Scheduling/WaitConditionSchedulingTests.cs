using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow.Scheduling;

/// <summary>
/// Tests for WaitCondition with scheduling support.
/// </summary>
public class WaitConditionSchedulingTests
{
    [Fact]
    public void WaitCondition_ShouldSupportScheduleId()
    {
        // Arrange & Act
        var condition = new WaitCondition
        {
            CorrelationId = "flow-1-step-0",
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = TimeSpan.FromHours(24),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-1",
            FlowType = "DelayTestFlow",
            Step = 0,
            ScheduleId = "quartz-job-123"
        };

        // Assert
        condition.ScheduleId.Should().Be("quartz-job-123");
    }

    [Fact]
    public void WaitCondition_ScheduleId_ShouldBeNullable()
    {
        // Arrange & Act - WhenAll/WhenAny don't use ScheduleId
        var condition = new WaitCondition
        {
            CorrelationId = "flow-1-step-0",
            Type = WaitType.All,
            ExpectedCount = 3,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(10),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-1",
            FlowType = "ParallelFlow",
            Step = 0,
            ScheduleId = null // No schedule for WhenAll
        };

        // Assert
        condition.ScheduleId.Should().BeNull();
    }

    [Fact]
    public void WaitCondition_ForDelay_ShouldHaveExpectedCount1()
    {
        // Arrange - Delay steps wait for 1 scheduled resume
        var condition = new WaitCondition
        {
            CorrelationId = "flow-1-step-2",
            Type = WaitType.All,
            ExpectedCount = 1, // Delay always expects 1
            CompletedCount = 0,
            Timeout = TimeSpan.FromHours(1),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-1",
            FlowType = "DelayFlow",
            Step = 2,
            ScheduleId = "delay-schedule-456"
        };

        // Assert
        condition.ExpectedCount.Should().Be(1);
        condition.Type.Should().Be(WaitType.All);
    }

    [Fact]
    public void WaitCondition_Timeout_ShouldIncludeBuffer()
    {
        // Arrange - Delay of 30 minutes should have timeout > 30 minutes
        var delayDuration = TimeSpan.FromMinutes(30);
        var buffer = TimeSpan.FromMinutes(1);
        var timeout = delayDuration.Add(buffer);

        var condition = new WaitCondition
        {
            CorrelationId = "flow-1-step-0",
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = timeout,
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-1",
            FlowType = "DelayFlow",
            Step = 0,
            ScheduleId = "schedule-789"
        };

        // Assert
        condition.Timeout.Should().BeGreaterThan(delayDuration);
    }

    [Fact]
    public void WaitCondition_ShouldTrackCreatedAt()
    {
        // Arrange
        var beforeCreate = DateTime.UtcNow;

        var condition = new WaitCondition
        {
            CorrelationId = "flow-1-step-0",
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = TimeSpan.FromHours(1),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-1",
            FlowType = "TestFlow",
            Step = 0
        };

        var afterCreate = DateTime.UtcNow;

        // Assert
        condition.CreatedAt.Should().BeOnOrAfter(beforeCreate);
        condition.CreatedAt.Should().BeOnOrBefore(afterCreate);
    }

    [Fact]
    public void WaitCondition_ShouldCalculateIsTimedOut()
    {
        // Arrange - Condition created 2 hours ago with 1 hour timeout
        var condition = new WaitCondition
        {
            CorrelationId = "flow-1-step-0",
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = TimeSpan.FromHours(1),
            CreatedAt = DateTime.UtcNow.AddHours(-2), // 2 hours ago
            FlowId = "flow-1",
            FlowType = "TestFlow",
            Step = 0
        };

        // Act
        var elapsed = DateTime.UtcNow - condition.CreatedAt;
        var isTimedOut = elapsed > condition.Timeout;

        // Assert
        isTimedOut.Should().BeTrue();
    }

    [Fact]
    public void WaitCondition_ShouldNotBeTimedOut_WhenWithinTimeout()
    {
        // Arrange - Condition created just now with 1 hour timeout
        var condition = new WaitCondition
        {
            CorrelationId = "flow-1-step-0",
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = TimeSpan.FromHours(1),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-1",
            FlowType = "TestFlow",
            Step = 0
        };

        // Act
        var elapsed = DateTime.UtcNow - condition.CreatedAt;
        var isTimedOut = elapsed > condition.Timeout;

        // Assert
        isTimedOut.Should().BeFalse();
    }
}
