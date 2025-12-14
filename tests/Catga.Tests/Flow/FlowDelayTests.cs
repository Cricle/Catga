using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow;

/// <summary>
/// TDD tests for Flow Delay/ScheduleAt functionality.
/// </summary>
public class FlowDelayTests
{
    #region Test Infrastructure

    public class TestState : IFlowState
    {
        public string FlowId { get; set; } = "test-flow";
        public string StateId { get; set; } = Guid.NewGuid().ToString();
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

    #region DSL API Tests (via FlowConfig)

    [Fact]
    public void Delay_ShouldAddDelayStep_ToFlow()
    {
        // Arrange & Act
        var config = new DelayFlowConfig();
        config.Build();

        // Assert
        config.Steps.Should().HaveCount(3);
        config.Steps[1].Type.Should().Be(StepType.Delay);
    }

    [Fact]
    public void Delay_ShouldStoreDelayDuration()
    {
        // Arrange & Act
        var config = new SimpleDelayConfig();
        config.Build();

        // Assert
        config.Steps.Should().HaveCount(1);
        config.Steps[0].Type.Should().Be(StepType.Delay);
        config.Steps[0].DelayDuration.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void ScheduleAt_ShouldAddScheduleAtStep_ToFlow()
    {
        // Arrange & Act
        var config = new ScheduleAtFlowConfig();
        config.Build();

        // Assert
        config.Steps.Should().HaveCount(2);
        config.Steps[0].Type.Should().Be(StepType.ScheduleAt);
    }

    [Fact]
    public void ScheduleAt_ShouldStoreTimeSelector()
    {
        // Arrange & Act
        var config = new ScheduleAtFlowConfig();
        config.Build();

        // Assert
        config.Steps[0].Type.Should().Be(StepType.ScheduleAt);
        config.Steps[0].GetScheduleTime.Should().NotBeNull();
    }

    [Fact]
    public void Delay_InsideIf_ShouldWork()
    {
        // Arrange & Act
        var config = new DelayInsideIfConfig();
        config.Build();

        // Assert
        config.Steps.Should().HaveCount(1);
        config.Steps[0].Type.Should().Be(StepType.If);
        config.Steps[0].ThenBranch.Should().HaveCount(2);
        config.Steps[0].ThenBranch![0].Type.Should().Be(StepType.Delay);
    }

    #endregion

    #region IFlowScheduler Interface Tests

    [Fact]
    public async Task FlowScheduler_ScheduleResumeAsync_ShouldReturnScheduleId()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var flowId = "test-flow";
        var stateId = "state-123";
        var resumeAt = DateTimeOffset.UtcNow.AddMinutes(30);

        scheduler.ScheduleResumeAsync(flowId, stateId, resumeAt, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("schedule-123"));

        // Act
        var scheduleId = await scheduler.ScheduleResumeAsync(flowId, stateId, resumeAt);

        // Assert
        scheduleId.Should().Be("schedule-123");
    }

    [Fact]
    public async Task FlowScheduler_CancelScheduledResumeAsync_ShouldReturnTrue_WhenCancelled()
    {
        // Arrange
        var scheduler = Substitute.For<IFlowScheduler>();
        var scheduleId = "schedule-123";

        scheduler.CancelScheduledResumeAsync(scheduleId, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        // Act
        var result = await scheduler.CancelScheduledResumeAsync(scheduleId);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Test Flow Configs

    private class DelayFlowConfig : FlowConfig<TestState>
    {
        public override string FlowId => "test-delay-flow";

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
        public override string FlowId => "test-scheduleat-flow";

        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow
                .ScheduleAt(s => s.PaymentDeadline)
                .Send(s => new CheckPaymentCommand { OrderId = s.OrderId });
        }
    }

    private class DelayInsideIfConfig : FlowConfig<TestState>
    {
        public override string FlowId => "delay-inside-if-flow";

        protected override void Configure(IFlowBuilder<TestState> flow)
        {
            flow
                .If(s => s.OrderId != null)
                    .Delay(TimeSpan.FromMinutes(10))
                    .Send(s => new SendReminderCommand { OrderId = s.OrderId })
                .EndIf();
        }
    }

    #endregion
}
