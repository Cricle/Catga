using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling.RealWorldScenarios;

/// <summary>
/// E2E tests for real-world order processing scenarios with scheduled delays.
/// </summary>
public class OrderProcessingE2ETests
{
    #region Test Infrastructure

    public class OrderState : IFlowState
    {
        public string? FlowId { get; set; }
        public string OrderId { get; set; } = $"ORD-{Guid.NewGuid():N}"[..12];
        public string CustomerId { get; set; } = "CUST-001";
        public decimal TotalAmount { get; set; }
        public OrderStatus Status { get; set; } = OrderStatus.Created;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PaymentDeadline { get; set; }
        public DateTime? ShippingDate { get; set; }
        public bool PaymentConfirmed { get; set; }
        public bool ReminderSent { get; set; }
        public int ReminderCount { get; set; }
        public string? TrackingNumber { get; set; }
    }

    public enum OrderStatus
    {
        Created,
        PendingPayment,
        PaymentReceived,
        Processing,
        Shipped,
        Delivered,
        Cancelled
    }

    // Commands
    public record CreateOrderCommand(string OrderId, string CustomerId, decimal Amount) : IRequest<string>;
    public record ProcessPaymentCommand(string OrderId) : IRequest<PaymentResult>;
    public record SendPaymentReminderCommand(string OrderId, int ReminderNumber) : IRequest;
    public record CancelOrderCommand(string OrderId, string Reason) : IRequest;
    public record ShipOrderCommand(string OrderId) : IRequest<string>;
    public record SendShippingNotificationCommand(string OrderId, string TrackingNumber) : IRequest;
    public record ConfirmDeliveryCommand(string OrderId) : IRequest<bool>;

    public record PaymentResult(bool Success, string? TransactionId);

    #endregion

    #region Scenario 1: Payment Reminder Flow

    /// <summary>
    /// Scenario: Order created -> Wait 24h -> Send reminder -> Wait 24h -> Cancel if unpaid
    /// </summary>
    [Fact]
    public async Task PaymentReminderFlow_ShouldSuspendAndScheduleReminder()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var scheduledTimes = new List<DateTimeOffset>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                scheduledTimes.Add(callInfo.ArgAt<DateTimeOffset>(2));
                return ValueTask.FromResult($"schedule-{scheduledTimes.Count}");
            });

        mediator.SendAsync(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<string>.Success("order-created"));

        var config = new PaymentReminderFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, PaymentReminderFlowConfig>(mediator, store, config, scheduler);
        var state = new OrderState { TotalAmount = 299.99m };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        scheduledTimes.Should().HaveCount(1);
        scheduledTimes[0].Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
    }

    private class PaymentReminderFlowConfig : FlowConfig<OrderState>
    {
        public override string FlowId => "payment-reminder-flow";

        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .Send<CreateOrderCommand, string>(s => new CreateOrderCommand(s.OrderId, s.CustomerId, s.TotalAmount))
                .Delay(TimeSpan.FromHours(24)) // Wait 24 hours for payment
                .If(s => !s.PaymentConfirmed)
                    .Send(s => new SendPaymentReminderCommand(s.OrderId, ++s.ReminderCount))
                    .Delay(TimeSpan.FromHours(24)) // Wait another 24 hours
                    .If(s => !s.PaymentConfirmed)
                        .Send(s => new CancelOrderCommand(s.OrderId, "Payment timeout"))
                    .EndIf()
                .EndIf();
        }
    }

    #endregion

    #region Scenario 2: Scheduled Shipping

    /// <summary>
    /// Scenario: Order paid -> Schedule shipping for specific date -> Ship -> Notify
    /// </summary>
    [Fact]
    public async Task ScheduledShipping_ShouldScheduleAtSpecificDate()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        DateTimeOffset capturedTime = default;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedTime = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("ship-schedule-123");
            });

        var config = new ScheduledShippingFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, ScheduledShippingFlowConfig>(mediator, store, config, scheduler);
        var shippingDate = DateTime.UtcNow.AddDays(3);
        var state = new OrderState
        {
            Status = OrderStatus.PaymentReceived,
            ShippingDate = shippingDate
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        capturedTime.UtcDateTime.Should().BeCloseTo(shippingDate, TimeSpan.FromSeconds(1));
    }

    private class ScheduledShippingFlowConfig : FlowConfig<OrderState>
    {
        public override string FlowId => "scheduled-shipping";

        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .ScheduleAt(s => s.ShippingDate ?? DateTime.UtcNow)
                .Send<ShipOrderCommand, string>(s => new ShipOrderCommand(s.OrderId))
                    .Into(s => s.TrackingNumber)
                .Send(s => new SendShippingNotificationCommand(s.OrderId, s.TrackingNumber!));
        }
    }

    #endregion

    #region Scenario 3: Delivery Confirmation with Timeout

    /// <summary>
    /// Scenario: Shipped -> Wait for delivery confirmation -> Auto-confirm after 7 days
    /// </summary>
    [Fact]
    public async Task DeliveryConfirmation_ShouldAutoConfirmAfterTimeout()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        DateTimeOffset capturedTime = default;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedTime = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("delivery-timeout-123");
            });

        var config = new DeliveryConfirmationFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, DeliveryConfirmationFlowConfig>(mediator, store, config, scheduler);
        var state = new OrderState
        {
            Status = OrderStatus.Shipped,
            TrackingNumber = "TRACK123456"
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        capturedTime.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
    }

    private class DeliveryConfirmationFlowConfig : FlowConfig<OrderState>
    {
        public override string FlowId => "delivery-confirmation";

        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .Delay(TimeSpan.FromDays(7)) // Wait 7 days for manual confirmation
                .Send<ConfirmDeliveryCommand, bool>(s => new ConfirmDeliveryCommand(s.OrderId));
        }
    }

    #endregion

    #region Scenario 4: High-Value Order Review

    /// <summary>
    /// Scenario: High-value orders require manual review before processing
    /// </summary>
    [Fact]
    public async Task HighValueOrder_ShouldWaitForReview()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var wasScheduled = false;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                wasScheduled = true;
                return ValueTask.FromResult("review-schedule");
            });

        mediator.SendAsync(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<string>.Success("created"));

        var config = new HighValueOrderFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, HighValueOrderFlowConfig>(mediator, store, config, scheduler);

        // High value order
        var state = new OrderState { TotalAmount = 5000m };
        var result = await executor.RunAsync(state);

        // Assert - High value should be suspended for review
        result.Status.Should().Be(DslFlowStatus.Suspended);
        wasScheduled.Should().BeTrue();
    }

    [Fact]
    public async Task LowValueOrder_ShouldProcessImmediately()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        mediator.SendAsync(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<string>.Success("created"));
        mediator.SendAsync(Arg.Any<ProcessPaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<PaymentResult>.Success(new PaymentResult(true, "TXN123")));

        var config = new HighValueOrderFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<OrderState, HighValueOrderFlowConfig>(mediator, store, config, scheduler);

        // Low value order
        var state = new OrderState { TotalAmount = 100m };
        var result = await executor.RunAsync(state);

        // Assert - Low value should complete without scheduling
        await scheduler.DidNotReceive().ScheduleResumeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private class HighValueOrderFlowConfig : FlowConfig<OrderState>
    {
        public override string FlowId => "high-value-order";

        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .Send<CreateOrderCommand, string>(s => new CreateOrderCommand(s.OrderId, s.CustomerId, s.TotalAmount))
                .If(s => s.TotalAmount >= 1000)
                    .Delay(TimeSpan.FromHours(4)) // Wait for manual review
                .EndIf()
                .Send<ProcessPaymentCommand, PaymentResult>(s => new ProcessPaymentCommand(s.OrderId));
        }
    }

    #endregion

    #region Helper Methods

    private static InMemoryTestStore CreateInMemoryStore() => new();

    private class InMemoryTestStore : IDslFlowStore
    {
        private readonly Dictionary<string, object> _flows = new();
        private readonly Dictionary<string, WaitCondition> _waitConditions = new();
        private readonly Dictionary<string, ForEachProgress> _forEachProgress = new();

        public Task<bool> CreateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default) where TState : class, IFlowState
        {
            _flows[snapshot.FlowId] = snapshot;
            return Task.FromResult(true);
        }

        public Task<FlowSnapshot<TState>?> GetAsync<TState>(string flowId, CancellationToken ct = default) where TState : class, IFlowState
            => Task.FromResult(_flows.TryGetValue(flowId, out var s) ? (FlowSnapshot<TState>?)s : null);

        public Task<bool> UpdateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default) where TState : class, IFlowState
        {
            _flows[snapshot.FlowId] = snapshot;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string flowId, CancellationToken ct = default)
            => Task.FromResult(_flows.Remove(flowId));

        public Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        {
            _waitConditions[correlationId] = condition;
            return Task.CompletedTask;
        }

        public Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
            => Task.FromResult(_waitConditions.TryGetValue(correlationId, out var c) ? c : null);

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
            var timedOut = _waitConditions.Values.Where(c => DateTime.UtcNow - c.CreatedAt > c.Timeout).ToList();
            return Task.FromResult<IReadOnlyList<WaitCondition>>(timedOut);
        }

        public Task SaveForEachProgressAsync(string flowId, int stepIndex, ForEachProgress progress, CancellationToken ct = default)
        {
            _forEachProgress[$"{flowId}:{stepIndex}"] = progress;
            return Task.CompletedTask;
        }

        public Task<ForEachProgress?> GetForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
            => Task.FromResult(_forEachProgress.TryGetValue($"{flowId}:{stepIndex}", out var p) ? p : null);

        public Task ClearForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
        {
            _forEachProgress.Remove($"{flowId}:{stepIndex}");
            return Task.CompletedTask;
        }
    }

    #endregion
}
