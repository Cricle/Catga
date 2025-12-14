using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling;

/// <summary>
/// TDD tests for Flow Delay/ScheduleAt execution in DslFlowExecutor.
/// </summary>
public class FlowDelayExecutionTests
{
    #region Test Infrastructure

    public class TestState : IFlowState
    {
        public string? FlowId { get; set; } = Guid.NewGuid().ToString();
        public string OrderId { get; set; } = "order-123";
        public DateTime PaymentDeadline { get; set; } = DateTime.UtcNow.AddHours(24);
        public bool ReminderSent { get; set; }
        public bool PaymentChecked { get; set; }
    }

    public record CreateOrderCommand : IRequest<string>
    {
        public string OrderId { get; init; } = "";
    }

    public record SendReminderCommand : IRequest
    {
        public string OrderId { get; init; } = "";
    }

    public record CheckPaymentCommand : IRequest<bool>
    {
        public string OrderId { get; init; } = "";
    }

    #endregion

    #region Delay Step Tests

    [Fact]
    public void DelayStep_ShouldHaveCorrectType()
    {
        // Arrange
        var config = new DelayFlowConfig();
        config.Build();

        // Assert
        config.Steps.Should().Contain(s => s.Type == StepType.Delay);
    }

    [Fact]
    public void DelayStep_ShouldStoreDelayDuration()
    {
        // Arrange
        var config = new DelayFlowConfig();
        config.Build();

        // Assert
        var delayStep = config.Steps.First(s => s.Type == StepType.Delay);
        delayStep.DelayDuration.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void ScheduleAtStep_ShouldHaveCorrectType()
    {
        // Arrange
        var config = new ScheduleAtFlowConfig();
        config.Build();

        // Assert
        config.Steps.Should().Contain(s => s.Type == StepType.ScheduleAt);
    }

    [Fact]
    public void ScheduleAtStep_ShouldHaveTimeSelector()
    {
        // Arrange
        var config = new ScheduleAtFlowConfig();
        config.Build();

        // Assert
        var scheduleStep = config.Steps.First(s => s.Type == StepType.ScheduleAt);
        scheduleStep.GetScheduleTime.Should().NotBeNull();
    }

    [Fact]
    public void ScheduleAtStep_ShouldEvaluateTimeSelector()
    {
        // Arrange
        var config = new ScheduleAtFlowConfig();
        config.Build();
        var state = new TestState { PaymentDeadline = new DateTime(2025, 12, 25, 12, 0, 0, DateTimeKind.Utc) };

        // Act
        var scheduleStep = config.Steps.First(s => s.Type == StepType.ScheduleAt);
        var scheduledTime = scheduleStep.GetScheduleTime!(state);

        // Assert
        scheduledTime.Should().Be(new DateTime(2025, 12, 25, 12, 0, 0, DateTimeKind.Utc));
    }

    #endregion

    #region Executor Integration Tests

    [Fact]
    public async Task Executor_WithoutScheduler_ShouldFail_OnDelayStep()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = Substitute.For<IDslFlowStore>();

        var config = new SimpleDelayConfig();
        config.Build();

        // No scheduler provided
        var executor = new DslFlowExecutor<TestState, SimpleDelayConfig>(mediator, store, config);
        var state = new TestState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("IFlowScheduler");
    }

    [Fact]
    public async Task Executor_WithScheduler_ShouldSuspend_OnDelayStep()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = Substitute.For<IDslFlowStore>();
        var scheduler = Substitute.For<IFlowScheduler>();

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("schedule-123"));

        var config = new SimpleDelayConfig();
        config.Build();

        var executor = new DslFlowExecutor<TestState, SimpleDelayConfig>(mediator, store, config, scheduler);
        var state = new TestState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
    }

    [Fact]
    public async Task Executor_ShouldCallScheduler_WithCorrectDelay()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = Substitute.For<IDslFlowStore>();
        var scheduler = Substitute.For<IFlowScheduler>();

        DateTimeOffset capturedResumeAt = default;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedResumeAt = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("schedule-123");
            });

        var config = new SimpleDelayConfig(); // 30 min delay
        config.Build();

        var executor = new DslFlowExecutor<TestState, SimpleDelayConfig>(mediator, store, config, scheduler);
        var state = new TestState();
        var beforeExecute = DateTimeOffset.UtcNow;

        // Act
        await executor.RunAsync(state);

        // Assert
        var expectedResumeAt = beforeExecute.AddMinutes(30);
        capturedResumeAt.Should().BeCloseTo(expectedResumeAt, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Executor_ScheduleAt_ShouldUseAbsoluteTime()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = Substitute.For<IDslFlowStore>();
        var scheduler = Substitute.For<IFlowScheduler>();

        DateTimeOffset capturedResumeAt = default;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedResumeAt = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("schedule-456");
            });

        var config = new ScheduleAtFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<TestState, ScheduleAtFlowConfig>(mediator, store, config, scheduler);
        var deadline = DateTime.UtcNow.AddDays(7);
        var state = new TestState { PaymentDeadline = deadline };

        // Act
        await executor.RunAsync(state);

        // Assert
        capturedResumeAt.UtcDateTime.Should().BeCloseTo(deadline, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Executor_ScheduleAt_PastTime_ShouldContinueImmediately()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = Substitute.For<IDslFlowStore>();
        var scheduler = Substitute.For<IFlowScheduler>();

        var config = new ScheduleAtFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<TestState, ScheduleAtFlowConfig>(mediator, store, config, scheduler);
        var state = new TestState { PaymentDeadline = DateTime.UtcNow.AddHours(-1) }; // Past time

        // Act
        var result = await executor.RunAsync(state);

        // Assert - Should complete without scheduling since time is in the past
        await scheduler.DidNotReceive().ScheduleResumeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Test Flow Configs

    private class DelayFlowConfig : FlowConfig<TestState>
    {
        public override string FlowId => "delay-test-flow";

        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow
                .Send<CreateOrderCommand, string>(s => new CreateOrderCommand { OrderId = s.OrderId })
                .Delay(TimeSpan.FromMinutes(30))
                .Send(s => new SendReminderCommand { OrderId = s.OrderId });
        }
    }

    private class SimpleDelayConfig : FlowConfig<TestState>
    {
        public override string FlowId => "simple-delay-flow";

        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow.Delay(TimeSpan.FromMinutes(30));
        }
    }

    private class ScheduleAtFlowConfig : FlowConfig<TestState>
    {
        public override string FlowId => "scheduleat-test-flow";

        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow
                .ScheduleAt(s => s.PaymentDeadline)
                .Send(s => new CheckPaymentCommand { OrderId = s.OrderId });
        }
    }

    #endregion
}
