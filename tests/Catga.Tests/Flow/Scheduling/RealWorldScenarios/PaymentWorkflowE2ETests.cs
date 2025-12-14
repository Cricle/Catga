using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow.Scheduling.RealWorldScenarios;

/// <summary>
/// E2E tests for payment workflow scenarios with retries and scheduled checks.
/// </summary>
public class PaymentWorkflowE2ETests
{
    #region Test Infrastructure

    public class PaymentState : IFlowState
    {
        public string? FlowId { get; set; }
        public string PaymentId { get; set; } = $"PAY-{Guid.NewGuid():N}"[..12];
        public string OrderId { get; set; } = "ORD-001";
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
        public PaymentMethod Method { get; set; } = PaymentMethod.CreditCard;
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
        public int RetryCount { get; set; }
        public string? TransactionId { get; set; }
        public string? FailureReason { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public bool RefundRequested { get; set; }
    }

    public enum PaymentMethod { CreditCard, BankTransfer, Crypto }
    public enum PaymentStatus { Pending, Processing, Completed, Failed, Refunded }

    // Commands
    public record InitiatePaymentCommand(string PaymentId, decimal Amount, PaymentMethod Method) : IRequest<PaymentInitResult>;
    public record CheckPaymentStatusCommand(string PaymentId) : IRequest<PaymentStatusResult>;
    public record RetryPaymentCommand(string PaymentId) : IRequest<PaymentInitResult>;
    public record NotifyPaymentFailedCommand(string PaymentId, string Reason) : IRequest;
    public record ProcessRefundCommand(string PaymentId) : IRequest<RefundResult>;

    public record PaymentInitResult(bool Initiated, string? TransactionId);
    public record PaymentStatusResult(PaymentStatus Status, string? TransactionId);
    public record RefundResult(bool Success, string? RefundId);

    #endregion

    #region Scenario 1: Bank Transfer Confirmation

    /// <summary>
    /// Scenario: Bank transfer initiated -> Check status every 1h -> Confirm or fail after 48h
    /// </summary>
    [Fact]
    public async Task BankTransfer_ShouldScheduleStatusChecks()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var scheduleCount = 0;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromResult($"check-{++scheduleCount}"));

        mediator.SendAsync(Arg.Any<InitiatePaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<PaymentInitResult>.Success(new PaymentInitResult(true, "TXN-BANK-001")));

        var config = new BankTransferFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<PaymentState, BankTransferFlowConfig>(mediator, store, config, scheduler);
        var state = new PaymentState { Amount = 1500m, Method = PaymentMethod.BankTransfer };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        scheduleCount.Should().Be(1); // First status check scheduled
    }

    private class BankTransferFlowConfig : FlowConfig<PaymentState>
    {
        public override string FlowId => "bank-transfer";

        protected override void Configure(IFlowBuilder<PaymentState> flow)
        {
            flow
                .Send<InitiatePaymentCommand, PaymentInitResult>(
                    s => new InitiatePaymentCommand(s.PaymentId, s.Amount, s.Method))
                    .Into(s => s.TransactionId, r => r.TransactionId)
                .Delay(TimeSpan.FromHours(1)) // Check after 1 hour
                .Send<CheckPaymentStatusCommand, PaymentStatusResult>(
                    s => new CheckPaymentStatusCommand(s.PaymentId));
        }
    }

    #endregion

    #region Scenario 2: Payment Retry with Backoff

    /// <summary>
    /// Scenario: Payment fails -> Retry after 5min, 15min, 1h -> Give up after 3 retries
    /// </summary>
    [Fact]
    public async Task PaymentRetry_ShouldUseExponentialBackoff()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var scheduledDelays = new List<TimeSpan>();
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var resumeAt = callInfo.ArgAt<DateTimeOffset>(2);
                scheduledDelays.Add(resumeAt - DateTimeOffset.UtcNow);
                return ValueTask.FromResult($"retry-{scheduledDelays.Count}");
            });

        // First attempt fails
        mediator.SendAsync(Arg.Any<InitiatePaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<PaymentInitResult>.Success(new PaymentInitResult(false, null)));

        var config = new PaymentRetryFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<PaymentState, PaymentRetryFlowConfig>(mediator, store, config, scheduler);
        var state = new PaymentState { Amount = 99.99m, RetryCount = 0 };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        scheduledDelays.Should().HaveCount(1);
        scheduledDelays[0].Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10));
    }

    private class PaymentRetryFlowConfig : FlowConfig<PaymentState>
    {
        public override string FlowId => "payment-retry";

        protected override void Configure(IFlowBuilder<PaymentState> flow)
        {
            flow
                .Send<InitiatePaymentCommand, PaymentInitResult>(
                    s => new InitiatePaymentCommand(s.PaymentId, s.Amount, s.Method))
                .If(s => s.Status == PaymentStatus.Failed && s.RetryCount < 3)
                    .Delay(TimeSpan.FromMinutes(5)) // First retry after 5 min
                    .Send<RetryPaymentCommand, PaymentInitResult>(s => new RetryPaymentCommand(s.PaymentId))
                .EndIf();
        }
    }

    #endregion

    #region Scenario 3: Crypto Payment Confirmation

    /// <summary>
    /// Scenario: Crypto payment requires blockchain confirmation - check every 10 min
    /// </summary>
    [Fact]
    public async Task CryptoPayment_ShouldWaitForBlockchainConfirmation()
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
                return ValueTask.FromResult("crypto-confirm");
            });

        mediator.SendAsync(Arg.Any<InitiatePaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(CatgaResult<PaymentInitResult>.Success(new PaymentInitResult(true, "0xabc123...")));

        var config = new CryptoPaymentFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<PaymentState, CryptoPaymentFlowConfig>(mediator, store, config, scheduler);
        var state = new PaymentState { Amount = 500m, Method = PaymentMethod.Crypto };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        var delay = capturedTime - DateTimeOffset.UtcNow;
        delay.Should().BeCloseTo(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30));
    }

    private class CryptoPaymentFlowConfig : FlowConfig<PaymentState>
    {
        public override string FlowId => "crypto-payment";

        protected override void Configure(IFlowBuilder<PaymentState> flow)
        {
            flow
                .Send<InitiatePaymentCommand, PaymentInitResult>(
                    s => new InitiatePaymentCommand(s.PaymentId, s.Amount, s.Method))
                    .Into(s => s.TransactionId, r => r.TransactionId)
                .Delay(TimeSpan.FromMinutes(10)) // Wait for blockchain confirmation
                .Send<CheckPaymentStatusCommand, PaymentStatusResult>(
                    s => new CheckPaymentStatusCommand(s.PaymentId));
        }
    }

    #endregion

    #region Scenario 4: Refund Processing with Approval

    /// <summary>
    /// Scenario: High-value refund requires manager approval - wait up to 24h
    /// </summary>
    [Fact]
    public async Task HighValueRefund_ShouldWaitForApproval()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = CreateInMemoryStore();
        var scheduler = Substitute.For<IFlowScheduler>();

        var wasScheduled = false;
        DateTimeOffset scheduledAt = default;
        scheduler.ScheduleResumeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                wasScheduled = true;
                scheduledAt = callInfo.ArgAt<DateTimeOffset>(2);
                return ValueTask.FromResult("refund-approval");
            });

        var config = new RefundApprovalFlowConfig();
        config.Build();

        var executor = new DslFlowExecutor<PaymentState, RefundApprovalFlowConfig>(mediator, store, config, scheduler);
        var state = new PaymentState
        {
            Amount = 2000m,
            RefundRequested = true,
            Status = PaymentStatus.Completed
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Status.Should().Be(DslFlowStatus.Suspended);
        wasScheduled.Should().BeTrue();
        var delay = scheduledAt - DateTimeOffset.UtcNow;
        delay.Should().BeCloseTo(TimeSpan.FromHours(24), TimeSpan.FromMinutes(1));
    }

    private class RefundApprovalFlowConfig : FlowConfig<PaymentState>
    {
        public override string FlowId => "refund-approval";

        protected override void Configure(IFlowBuilder<PaymentState> flow)
        {
            flow
                .If(s => s.Amount >= 500 && s.RefundRequested)
                    .Delay(TimeSpan.FromHours(24)) // Wait for manager approval
                .EndIf()
                .Send<ProcessRefundCommand, RefundResult>(s => new ProcessRefundCommand(s.PaymentId));
        }
    }

    #endregion

    #region Helper

    private static InMemoryTestStore CreateInMemoryStore() => new();

    private class InMemoryTestStore : IDslFlowStore
    {
        private readonly Dictionary<string, object> _flows = new();
        private readonly Dictionary<string, WaitCondition> _waitConditions = new();
        private readonly Dictionary<string, ForEachProgress> _forEachProgress = new();

        public Task<bool> CreateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default) where TState : class, IFlowState
        { _flows[snapshot.FlowId] = snapshot; return Task.FromResult(true); }
        public Task<FlowSnapshot<TState>?> GetAsync<TState>(string flowId, CancellationToken ct = default) where TState : class, IFlowState
            => Task.FromResult(_flows.TryGetValue(flowId, out var s) ? (FlowSnapshot<TState>?)s : null);
        public Task<bool> UpdateAsync<TState>(FlowSnapshot<TState> snapshot, CancellationToken ct = default) where TState : class, IFlowState
        { _flows[snapshot.FlowId] = snapshot; return Task.FromResult(true); }
        public Task<bool> DeleteAsync(string flowId, CancellationToken ct = default) => Task.FromResult(_flows.Remove(flowId));
        public Task SetWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        { _waitConditions[correlationId] = condition; return Task.CompletedTask; }
        public Task<WaitCondition?> GetWaitConditionAsync(string correlationId, CancellationToken ct = default)
            => Task.FromResult(_waitConditions.TryGetValue(correlationId, out var c) ? c : null);
        public Task UpdateWaitConditionAsync(string correlationId, WaitCondition condition, CancellationToken ct = default)
        { _waitConditions[correlationId] = condition; return Task.CompletedTask; }
        public Task ClearWaitConditionAsync(string correlationId, CancellationToken ct = default)
        { _waitConditions.Remove(correlationId); return Task.CompletedTask; }
        public Task<IReadOnlyList<WaitCondition>> GetTimedOutWaitConditionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WaitCondition>>(_waitConditions.Values.Where(c => DateTime.UtcNow - c.CreatedAt > c.Timeout).ToList());
        public Task SaveForEachProgressAsync(string flowId, int stepIndex, ForEachProgress progress, CancellationToken ct = default)
        { _forEachProgress[$"{flowId}:{stepIndex}"] = progress; return Task.CompletedTask; }
        public Task<ForEachProgress?> GetForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
            => Task.FromResult(_forEachProgress.TryGetValue($"{flowId}:{stepIndex}", out var p) ? p : null);
        public Task ClearForEachProgressAsync(string flowId, int stepIndex, CancellationToken ct = default)
        { _forEachProgress.Remove($"{flowId}:{stepIndex}"); return Task.CompletedTask; }
    }

    #endregion
}
