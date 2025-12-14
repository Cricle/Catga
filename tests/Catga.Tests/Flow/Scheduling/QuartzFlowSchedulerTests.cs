using Catga.Flow;
using FluentAssertions;
using NSubstitute;
using Quartz;

namespace Catga.Tests.Flow.Scheduling;

/// <summary>
/// TDD tests for QuartzFlowScheduler.
/// </summary>
public class QuartzFlowSchedulerTests
{
    [Fact]
    public async Task ScheduleResumeAsync_ShouldReturnScheduleId()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("schedule-123"));

        // Act
        var result = await scheduler.ScheduleResumeAsync("flow-1", "state-1", DateTimeOffset.UtcNow.AddMinutes(30));

        // Assert
        result.Should().Be("schedule-123");
    }

    [Fact]
    public async Task ScheduleResumeAsync_ShouldAcceptFutureTime()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var futureTime = DateTimeOffset.UtcNow.AddHours(24);
        DateTimeOffset capturedTime = default;

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedTime = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("schedule-456");
            });

        // Act
        await scheduler.ScheduleResumeAsync("flow-1", "state-1", futureTime);

        // Assert
        capturedTime.Should().BeCloseTo(futureTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CancelScheduledResumeAsync_ShouldReturnTrue_WhenCancelled()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.CancelScheduledResumeAsync("schedule-123", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        // Act
        var result = await scheduler.CancelScheduledResumeAsync("schedule-123");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CancelScheduledResumeAsync_ShouldReturnFalse_WhenNotFound()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.CancelScheduledResumeAsync("non-existent", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        // Act
        var result = await scheduler.CancelScheduledResumeAsync("non-existent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ScheduleResumeAsync_ShouldHandleMultipleSchedules()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var scheduleIds = new List<string>();
        var counter = 0;

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var id = $"schedule-{++counter}";
                scheduleIds.Add(id);
                return ValueTask.FromResult(id);
            });

        // Act
        var id1 = await scheduler.ScheduleResumeAsync("flow-1", "state-1", DateTimeOffset.UtcNow.AddMinutes(10));
        var id2 = await scheduler.ScheduleResumeAsync("flow-2", "state-2", DateTimeOffset.UtcNow.AddMinutes(20));
        var id3 = await scheduler.ScheduleResumeAsync("flow-3", "state-3", DateTimeOffset.UtcNow.AddMinutes(30));

        // Assert
        scheduleIds.Should().HaveCount(3);
        id1.Should().Be("schedule-1");
        id2.Should().Be("schedule-2");
        id3.Should().Be("schedule-3");
    }
}
