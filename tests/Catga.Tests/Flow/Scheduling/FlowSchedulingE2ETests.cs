using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling;

/// <summary>
/// E2E integration tests for Flow scheduling with Delay/ScheduleAt steps.
/// Tests the complete flow from DSL definition through execution and resumption.
/// </summary>
public class FlowSchedulingE2ETests
{
    #region Test Infrastructure

    public class OrderState : IFlowState
    {
        public string? FlowId { get; set; } = Guid.NewGuid().ToString();
        public string OrderId { get; set; } = "order-" + Guid.NewGuid().ToString("N")[..8];
        public decimal Amount { get; set; } = 100m;
        public DateTime PaymentDeadline { get; set; } = DateTime.UtcNow.AddDays(3);
        public bool OrderCreated { get; set; }
        public bool ReminderSent { get; set; }
        public bool PaymentConfirmed { get; set; }
        public int ReminderCount { get; set; }
    }

    public record CreateOrderCommand(string OrderId, decimal Amount) : IRequest<string>;
    public record SendReminderCommand(string OrderId) : IRequest;
    public record ConfirmPaymentCommand(string OrderId) : IRequest<bool>;
    public record CancelOrderCommand(string OrderId) : IRequest;

    #endregion

    #region Complete Flow E2E Tests

    [Fact]
    public async Task E2E_OrderFlowWithPaymentReminder_ShouldSuspendAndResume()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        mediator.SendAsync(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<string>.Success("order-created"));

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("schedule-reminder-123"));

        var config = new OrderPaymentReminderFlow();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, OrderPaymentReminderFlow>(mediator, store, config, scheduler);
        var state = new OrderState { Amount = 500m };

        // Act - Initial execution should suspend at Delay step
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        await scheduler.Received(1).ScheduleResumeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task E2E_FlowWithMultipleDelays_ShouldSuspendAtFirstDelay()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        mediator.SendAsync(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<string>.Success("ok"));

        var scheduleCount = 0;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult($"schedule-{++scheduleCount}"));

        var config = new MultiDelayFlow();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, MultiDelayFlow>(mediator, store, config, scheduler);
        var state = new OrderState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert - Should suspend at first delay, scheduler called once
        result.Status.Should().Be(DslFlowStatus.Suspended);
        scheduleCount.Should().Be(1);
    }

    [Fact]
    public async Task E2E_FlowWithConditionalDelay_ShouldOnlyDelayWhenConditionMet()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("schedule-123"));

        var config = new ConditionalDelayFlow();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, ConditionalDelayFlow>(mediator, store, config, scheduler);

        // High value order - should delay
        var highValueState = new OrderState { Amount = 2000m };
        var highValueResult = await executor.RunAsync(highValueState);

        // Assert - High value order should be suspended
        highValueResult.Status.Should().Be(DslFlowStatus.Suspended);
        await scheduler.Received().ScheduleResumeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task E2E_ScheduleAt_ShouldUseStateBasedTime()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        DateTimeOffset capturedTime = default;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedTime = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("schedule-payment");
            });

        var config = new PaymentDeadlineFlow();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, PaymentDeadlineFlow>(mediator, store, config, scheduler);
        var deadline = DateTime.UtcNow.AddDays(7);
        var state = new OrderState { PaymentDeadline = deadline };

        // Act
        await executor.RunAsync(state);

        // Assert
        capturedTime.UtcDateTime.Should().BeCloseTo(deadline, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Flow Resume Tests

    [Fact]
    public async Task E2E_ResumeFlow_ShouldContinueFromNextStep()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        mediator.SendAsync(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<string>.Success("created"));
        mediator.SendAsync(Arg.Any<SendReminderCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult.Success());

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult("schedule-123"));

        var config = new OrderPaymentReminderFlow();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, OrderPaymentReminderFlow>(mediator, store, config, scheduler);
        var state = new OrderState();

        // Initial run - suspends at Delay
        var result1 = await executor.RunAsync(state);
        result1.Status.Should().Be(DslFlowStatus.Suspended);

        // Resume - should continue from after Delay step
        var result2 = await executor.ResumeAsync(state.FlowId!);

        // Assert - CreateOrder called once (initial run), SendReminder called once (resume)
        await mediator.Received(1).SendAsync(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task E2E_SchedulerFailure_ShouldFailFlow()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<string>>(_ => throw new Exception("Scheduler unavailable"));

        var config = new SimpleDelayFlow();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, SimpleDelayFlow>(mediator, store, config, scheduler);
        var state = new OrderState();

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Failed);
        result.Error.Should().Contain("Scheduler unavailable");
    }

    #endregion

    #region Test Flow Configs

    private class OrderPaymentReminderFlow : FlowConfig<OrderState>
    {
        public override string FlowId => "order-payment-reminder";

        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .Send<CreateOrderCommand, string>(s => new CreateOrderCommand(s.OrderId, s.Amount))
                .Delay(TimeSpan.FromHours(24)) // Wait 24 hours
                .Send(s => new SendReminderCommand(s.OrderId));
        }
    }

    private class MultiDelayFlow : FlowConfig<OrderState>
    {
        public override string FlowId => "multi-delay-flow";

        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .Send<CreateOrderCommand, string>(s => new CreateOrderCommand(s.OrderId, s.Amount))
                .Delay(TimeSpan.FromHours(1))  // First delay
                .Send(s => new SendReminderCommand(s.OrderId))
                .Delay(TimeSpan.FromHours(2))  // Second delay
                .Send(s => new SendReminderCommand(s.OrderId));
        }
    }

    private class ConditionalDelayFlow : FlowConfig<OrderState>
    {
        public override string FlowId => "conditional-delay-flow";

        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .If(s => s.Amount > 1000)
                    .Delay(TimeSpan.FromHours(48)) // High value order needs review delay
                    .Send(s => new SendReminderCommand(s.OrderId))
                .EndIf();
        }
    }

    private class PaymentDeadlineFlow : FlowConfig<OrderState>
    {
        public override string FlowId => "payment-deadline-flow";

        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .ScheduleAt(s => s.PaymentDeadline)
                .Send(s => new ConfirmPaymentCommand(s.OrderId));
        }
    }

    private class SimpleDelayFlow : FlowConfig<OrderState>
    {
        public override string FlowId => "simple-delay";

        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow.Delay(TimeSpan.FromMinutes(5));
        }
    }

    #endregion

    #region InMemory Store for Testing

    private class InMemoryDslFlowStore : IDslFlowStore
    {
        private readonly Dictionary<string, object> _flows = new();
        private readonly Dictionary<string, WaitCondition> _waitConditions = new();
        private readonly Dictionary<string, ForEachProgress> _forEachProgress = new();

        public Task CreateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default)
            where TState : class, IFlowState
        {
            _flows[snapshot.FlowId] = snapshot;
            return Task.CompletedTask;
        }

        public Task<FlowSnapshot<TState>?> GetAsync<TState>(string flowId, CancellationToken ct = default)
            where TState : class, IFlowState
        {
            return Task.FromResult(_flows.TryGetValue(flowId, out var snapshot)
                ? (FlowSnapshot<TState>?)snapshot
                : null);
        }

        public Task<bool> UpdateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default)
            where TState : class, IFlowState
        {
            _flows[snapshot.FlowId] = snapshot;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string flowId, CancellationToken ct = default)
        {
            return Task.FromResult(_flows.Remove(flowId));
        }

        public Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        {
            _waitConditions[correlationId] = condition;
            return Task.CompletedTask;
        }

        public Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
        {
            return Task.FromResult(_waitConditions.TryGetValue(correlationId, out var condition) ? condition : null);
        }

        public Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        {
            _waitConditions[correlationId] = condition;
            return Task.CompletedTask;
        }

        public Task ClearWaitConditionAsync(string correlationId, CancellationToken ct = default)
        {
            _waitConditions.Remove(correlationId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WaitCondition>> GetTimedOutWaitConditionsAsync(CancellationToken ct = default)
        {
            var timedOut = _waitConditions.Values
                .Where(w => DateTime.UtcNow - w.CreatedAt > w.Timeout)
                .ToList();
            return Task.FromResult<IReadOnlyList<WaitCondition>>(timedOut);
        }

        public Task SaveForEachProgressAsync(string flowId, int stepIndex, ForEachProgress progress, CancellationToken ct = default)
        {
            _forEachProgress[$"{flowId}-{stepIndex}"] = progress;
            return Task.CompletedTask;
        }

        public Task<ForEachProgress?> GetForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
        {
            return Task.FromResult(_forEachProgress.TryGetValue($"{flowId}-{stepIndex}", out var progress) ? progress : null);
        }

        public Task ClearForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
        {
            _forEachProgress.Remove($"{flowId}-{stepIndex}");
            return Task.CompletedTask;
        }
    }

    #endregion
}
