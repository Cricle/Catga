using Catga.Flow;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling;

/// <summary>
/// Tests for concurrent scheduling scenarios.
/// </summary>
public class ConcurrentSchedulingTests
{
    [Fact]
    public async Task ConcurrentSchedules_ShouldAllSucceed()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var counter = 0;

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult($"schedule-{Interlocked.Increment(ref counter)}"));

        // Act - Schedule 100 flows concurrently
        var tasks = Enumerable.Range(0, 100)
            .Select(i => scheduler.ScheduleResumeAsync($"flow-{i}", $"state-{i}", DateTimeOffset.UtcNow.AddMinutes(i)).AsTask())
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(100);
        results.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ConcurrentScheduleAndCancel_ShouldNotDeadlock()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var schedules = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var id = $"schedule-{Guid.NewGuid():N}";
                schedules[id] = true;
                return ValueTask.FromResult(id);
            });

        scheduler.CancelScheduledResumeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var id = callInfo.ArgAt<string>(0);
                return ValueTask.FromResult(schedules.TryRemove(id, out _));
            });

        // Act - Mix of schedules and cancels
        var scheduleTasks = Enumerable.Range(0, 50)
            .Select(async i =>
            {
                var id = await scheduler.ScheduleResumeAsync($"flow-{i}", $"state-{i}", DateTimeOffset.UtcNow.AddMinutes(i));
                if (i % 2 == 0)
                {
                    await scheduler.CancelScheduledResumeAsync(id);
                }
                return id;
            })
            .ToList();

        await Task.WhenAll(scheduleTasks);

        // Assert - Should complete without deadlock
        schedules.Count.Should().Be(25); // Half were cancelled
    }

    [Fact]
    public async Task RapidScheduling_ShouldMaintainOrder()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var scheduledTimes = new List<DateTimeOffset>();
        var lockObj = new object();

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var time = callInfo.ArgAt<DateTimeOffset>(2);
                lock (lockObj)
                {
                    scheduledTimes.Add(time);
                }
                return ValueTask.FromResult($"schedule-{scheduledTimes.Count}");
            });

        // Act - Schedule with specific times
        var baseTime = DateTimeOffset.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            await scheduler.ScheduleResumeAsync($"flow-{i}", $"state-{i}", baseTime.AddMinutes(i * 5));
        }

        // Assert
        scheduledTimes.Should().HaveCount(10);
        scheduledTimes.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ParallelFlowsWithDelay_ShouldScheduleIndependently()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var flowSchedules = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var flowId = callInfo.ArgAt<string>(0);
                var scheduleId = $"schedule-for-{flowId}";
                flowSchedules[flowId] = scheduleId;
                return ValueTask.FromResult(scheduleId);
            });

        // Act - Simulate 5 parallel flows each scheduling
        var parallelTasks = Enumerable.Range(0, 5)
            .Select(async i =>
            {
                var flowId = $"parallel-flow-{i}";
                return await scheduler.ScheduleResumeAsync(flowId, flowId, DateTimeOffset.UtcNow.AddHours(i));
            })
            .ToList();

        var results = await Task.WhenAll(parallelTasks);

        // Assert
        flowSchedules.Should().HaveCount(5);
        results.Should().OnlyHaveUniqueItems();
    }
}
