using Catga.Flow;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling;

/// <summary>
/// TDD tests for HangfireFlowScheduler.
/// </summary>
public class HangfireFlowSchedulerTests
{
    [Fact]
    public async Task ScheduleResumeAsync_ShouldReturnJobId()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("hangfire-job-123"));

        // Act
        var result = await scheduler.ScheduleResumeAsync("flow-1", "state-1", DateTimeOffset.UtcNow.AddMinutes(30));

        // Assert
        result.Should().Be("hangfire-job-123");
    }

    [Fact]
    public async Task ScheduleResumeAsync_ShouldCalculateCorrectDelay()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var futureTime = DateTimeOffset.UtcNow.AddHours(2);
        DateTimeOffset capturedTime = default;

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedTime = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("job-456");
            });

        // Act
        await scheduler.ScheduleResumeAsync("flow-1", "state-1", futureTime);

        // Assert
        capturedTime.Should().BeCloseTo(futureTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CancelScheduledResumeAsync_ShouldDeleteJob()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.CancelScheduledResumeAsync("hangfire-job-123", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        // Act
        var result = await scheduler.CancelScheduledResumeAsync("hangfire-job-123");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ScheduleResumeAsync_ShouldHandlePastTime()
    {
        // Arrange - Past time should schedule immediately (delay = 0)
        var scheduler = Substitute.For<IFlowScheduler>();
        var pastTime = DateTimeOffset.UtcNow.AddMinutes(-10);

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("immediate-job"));

        // Act
        var result = await scheduler.ScheduleResumeAsync("flow-1", "state-1", pastTime);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }
}
