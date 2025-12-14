using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling;

/// <summary>
/// Edge case tests for Delay/ScheduleAt steps.
/// </summary>
public class DelayStepEdgeCaseTests
{
    public class TestState : IFlowState
    {
        public string? FlowId { get; set; } = Guid.NewGuid().ToString();
        public DateTime ScheduledTime { get; set; } = DateTime.UtcNow.AddHours(1);
    }

    public record TestCommand(string Id) : IRequest;

    #region Zero/Negative Delay Tests

    [Fact]
    public void DelayStep_WithZeroDuration_ShouldBeValid()
    {
        // Arrange
        var config = new ZeroDelayConfig();
        config.Build();

        // Assert
        var delayStep = config.Steps.First(s => s.Type == StepType.Delay);
        delayStep.DelayDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Executor_WithZeroDelay_ShouldContinueImmediately()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = Substitute.For<IDslFlowStore>();
        var scheduler = Substitute.For<IFlowScheduler>();

        var config = new ZeroDelayConfig();
        config.Build();

        var executor = new DslFlowExecutor<TestState, ZeroDelayConfig>(mediator, store, config, scheduler);
        var state = new TestState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert - Zero delay should complete without scheduling
        await scheduler.DidNotReceive().ScheduleResumeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region ScheduleAt Edge Cases

    [Fact]
    public async Task ScheduleAt_WithPastTime_ShouldContinueImmediately()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = Substitute.For<IDslFlowStore>();
        var scheduler = Substitute.For<IFlowScheduler>();

        var config = new ScheduleAtConfig();
        config.Build();

        var executor = new DslFlowExecutor<TestState, ScheduleAtConfig>(mediator, store, config, scheduler);
        var state = new TestState { ScheduledTime = DateTime.UtcNow.AddHours(-1) }; // Past

        // Act
        await executor.RunAsync(state);

        // Assert - Past time should continue without scheduling
        await scheduler.DidNotReceive().ScheduleResumeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScheduleAt_WithCurrentTime_ShouldContinueImmediately()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = Substitute.For<IDslFlowStore>();
        var scheduler = Substitute.For<IFlowScheduler>();

        var config = new ScheduleAtConfig();
        config.Build();

        var executor = new DslFlowExecutor<TestState, ScheduleAtConfig>(mediator, store, config, scheduler);
        var state = new TestState { ScheduledTime = DateTime.UtcNow }; // Now

        // Act
        await executor.RunAsync(state);

        // Assert - Current time should continue without scheduling
        await scheduler.DidNotReceive().ScheduleResumeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScheduleAt_WithFarFutureTime_ShouldSchedule()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = Substitute.For<IDslFlowStore>();
        var scheduler = Substitute.For<IFlowScheduler>();

        DateTimeOffset capturedTime = default;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedTime = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("schedule-far-future");
            });

        var config = new ScheduleAtConfig();
        config.Build();

        var executor = new DslFlowExecutor<TestState, ScheduleAtConfig>(mediator, store, config, scheduler);
        var farFuture = DateTime.UtcNow.AddYears(1);
        var state = new TestState { ScheduledTime = farFuture };

        // Act
        await executor.RunAsync(state);

        // Assert
        capturedTime.UtcDateTime.Should().BeCloseTo(farFuture, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Very Short Delays

    [Fact]
    public async Task Delay_WithMilliseconds_ShouldSchedule()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = Substitute.For<IDslFlowStore>();
        var scheduler = Substitute.For<IFlowScheduler>();

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("schedule-ms"));

        var config = new MillisecondDelayConfig();
        config.Build();

        var executor = new DslFlowExecutor<TestState, MillisecondDelayConfig>(mediator, store, config, scheduler);
        var state = new TestState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert - Even millisecond delays should schedule
        result.Status.Should().Be(DslFlowStatus.Suspended);
    }

    #endregion

    #region Very Long Delays

    [Fact]
    public void DelayStep_WithLongDuration_ShouldBeValid()
    {
        // Arrange
        var config = new LongDelayConfig();
        config.Build();

        // Assert
        var delayStep = config.Steps.First(s => s.Type == StepType.Delay);
        delayStep.DelayDuration.Should().Be(TimeSpan.FromDays(365));
    }

    #endregion

    #region Test Configs

    private class ZeroDelayConfig : FlowConfig<TestState>
    {
        public override string FlowId => "zero-delay";

        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Delay(TimeSpan.Zero);
        }
    }

    private class ScheduleAtConfig : FlowConfig<TestState>
    {
        public override string FlowId => "schedule-at";

        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.ScheduleAt(s => s.ScheduledTime);
        }
    }

    private class MillisecondDelayConfig : FlowConfig<TestState>
    {
        public override string FlowId => "ms-delay";

        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private class LongDelayConfig : FlowConfig<TestState>
    {
        public override string FlowId => "long-delay";

        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Delay(TimeSpan.FromDays(365));
        }
    }

    #endregion
}
