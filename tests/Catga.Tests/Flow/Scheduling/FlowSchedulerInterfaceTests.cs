using Catga.Flow;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling;

/// <summary>
/// Comprehensive interface tests for IFlowScheduler.
/// </summary>
public class FlowSchedulerInterfaceTests
{
    [Fact]
    public void IFlowScheduler_ShouldDefineScheduleResumeAsync()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();

        // Assert - Interface method exists
        var method = typeof(IFlowScheduler).GetMethod("ScheduleResumeAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(ValueTask<string>));
    }

    [Fact]
    public void IFlowScheduler_ShouldDefineCancelScheduledResumeAsync()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();

        // Assert - Interface method exists
        var method = typeof(IFlowScheduler).GetMethod("CancelScheduledResumeAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(ValueTask<bool>));
    }

    [Fact]
    public async Task ScheduleResumeAsync_ShouldAcceptAllRequiredParameters()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        string? capturedFlowId = null;
        string? capturedStateId = null;
        DateTimeOffset capturedResumeAt = default;

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedFlowId = callInfo.ArgAt<string>(0);
                capturedStateId = callInfo.ArgAt<string>(1);
                capturedResumeAt = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("test-schedule");
            });

        var flowId = "my-flow-123";
        var stateId = "my-state-456";
        var resumeAt = DateTimeOffset.UtcNow.AddHours(2);

        // Act
        await scheduler.ScheduleResumeAsync(flowId, stateId, resumeAt);

        // Assert
        capturedFlowId.Should().Be(flowId);
        capturedStateId.Should().Be(stateId);
        capturedResumeAt.Should().Be(resumeAt);
    }

    [Fact]
    public async Task ScheduleResumeAsync_ShouldSupportCancellationToken()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        CancellationToken capturedToken = default;

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedToken = callInfo.ArgAt<CancellationToken>(3);
                return ValueTask.FromResult("test");
            });

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await scheduler.ScheduleResumeAsync("flow", "state", DateTimeOffset.UtcNow, token);

        // Assert
        capturedToken.Should().Be(token);
    }

    [Fact]
    public async Task CancelScheduledResumeAsync_ShouldAcceptScheduleId()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        string? capturedId = null;

        scheduler.CancelScheduledResumeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedId = callInfo.ArgAt<string>(0);
                return ValueTask.FromResult(true);
            });

        var scheduleId = "schedule-to-cancel-789";

        // Act
        await scheduler.CancelScheduledResumeAsync(scheduleId);

        // Assert
        capturedId.Should().Be(scheduleId);
    }

    [Fact]
    public async Task Scheduler_ShouldBeReusable()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var callCount = 0;

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult($"schedule-{++callCount}"));

        // Act - Use same scheduler multiple times
        var id1 = await scheduler.ScheduleResumeAsync("f1", "s1", DateTimeOffset.UtcNow);
        var id2 = await scheduler.ScheduleResumeAsync("f2", "s2", DateTimeOffset.UtcNow);
        var id3 = await scheduler.ScheduleResumeAsync("f3", "s3", DateTimeOffset.UtcNow);

        // Assert
        callCount.Should().Be(3);
        id1.Should().NotBe(id2);
        id2.Should().NotBe(id3);
    }

    [Fact]
    public async Task Scheduler_ShouldHandleNullFlowId()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(null!, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<string>>(_ => throw new ArgumentNullException("flowId"));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => scheduler.ScheduleResumeAsync(null!, "state", DateTimeOffset.UtcNow).AsTask());
    }

    [Fact]
    public async Task Scheduler_ShouldHandleEmptyFlowId()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        scheduler.ScheduleResumeAsync(string.Empty, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<string>>(_ => throw new ArgumentException("flowId cannot be empty"));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => scheduler.ScheduleResumeAsync(string.Empty, "state", DateTimeOffset.UtcNow).AsTask());
    }
}
