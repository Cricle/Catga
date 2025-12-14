using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling;

/// <summary>
/// Tests for schedule cancellation scenarios.
/// </summary>
public class ScheduleCancellationTests
{
    public class TestState : IFlowState
    {
        public string? FlowId { get; set; } = Guid.NewGuid().ToString();
        public string OrderId { get; set; } = "order-123";
    }

    public record ProcessCommand(string Id) : IRequest;

    [Fact]
    public async Task CancelScheduledResume_ShouldReturnTrue_WhenScheduleExists()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("schedule-to-cancel"));
        scheduler.CancelScheduledResumeAsync("schedule-to-cancel", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        // Act
        var scheduleId = await scheduler.ScheduleResumeAsync("flow-1", "state-1", DateTimeOffset.UtcNow.AddHours(1));
        var cancelled = await scheduler.CancelScheduledResumeAsync(scheduleId);

        // Assert
        cancelled.Should().BeTrue();
    }

    [Fact]
    public async Task CancelScheduledResume_ShouldReturnFalse_WhenScheduleDoesNotExist()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.CancelScheduledResumeAsync("non-existent-schedule", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        // Act
        var cancelled = await scheduler.CancelScheduledResumeAsync("non-existent-schedule");

        // Assert
        cancelled.Should().BeFalse();
    }

    [Fact]
    public async Task CancelScheduledResume_ShouldHandleMultipleCancellations()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var activeSchedules = new HashSet<string>();

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var id = $"schedule-{Guid.NewGuid():N}";
                activeSchedules.Add(id);
                return ValueTask.FromResult(id);
            });

        scheduler.CancelScheduledResumeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var id = callInfo.ArgAt<string>(0);
                return ValueTask.FromResult(activeSchedules.Remove(id));
            });

        // Schedule 3
        var id1 = await scheduler.ScheduleResumeAsync("f1", "s1", DateTimeOffset.UtcNow.AddMinutes(10));
        var id2 = await scheduler.ScheduleResumeAsync("f2", "s2", DateTimeOffset.UtcNow.AddMinutes(20));
        var id3 = await scheduler.ScheduleResumeAsync("f3", "s3", DateTimeOffset.UtcNow.AddMinutes(30));

        activeSchedules.Should().HaveCount(3);

        // Cancel 2
        await scheduler.CancelScheduledResumeAsync(id1);
        await scheduler.CancelScheduledResumeAsync(id3);

        // Assert
        activeSchedules.Should().HaveCount(1);
        activeSchedules.Should().Contain(id2);
    }

    [Fact]
    public async Task CancelScheduledResume_ShouldBeIdempotent()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var cancelCount = 0;

        scheduler.CancelScheduledResumeAsync("schedule-123", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancelCount++;
                return ValueTask.FromResult(cancelCount == 1); // Only first cancel succeeds
            });

        // Act
        var result1 = await scheduler.CancelScheduledResumeAsync("schedule-123");
        var result2 = await scheduler.CancelScheduledResumeAsync("schedule-123");
        var result3 = await scheduler.CancelScheduledResumeAsync("schedule-123");

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeFalse();
        result3.Should().BeFalse();
    }

    [Fact]
    public async Task WaitCondition_ShouldStoreScheduleId_ForCancellation()
    {
        // Arrange
        var waitCondition = new WaitCondition
        {
            CorrelationId = "flow-1-step-0",
            Type = WaitType.All,
            ExpectedCount = 1,
            CompletedCount = 0,
            Timeout = TimeSpan.FromHours(1),
            CreatedAt = DateTime.UtcNow,
            FlowId = "flow-1",
            FlowType = "TestFlow",
            Step = 0,
            ScheduleId = "quartz-schedule-123"
        };

        // Assert
        waitCondition.ScheduleId.Should().Be("quartz-schedule-123");
    }
}
