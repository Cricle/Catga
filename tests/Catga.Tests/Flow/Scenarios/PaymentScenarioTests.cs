using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Scenario tests for payment processing workflows
/// Tests payment, refund, and retry scenarios
/// </summary>
public class PaymentScenarioTests
{
    [Fact]
    public async Task Payment_SuccessfulTransaction_ShouldComplete()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new SuccessfulPaymentFlow();
        var executor = new DslFlowExecutor<PaymentScenarioState, SuccessfulPaymentFlow>(mediator, store, config);

        var state = new PaymentScenarioState
        {
            FlowId = "payment-success",
            TransactionId = "TXN-001",
            Amount = 1000m,
            Status = "Pending"
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Status.Should().Be("Completed");
        state.PaymentAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Payment_WithRetry_ShouldRecoverFromFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new PaymentWithRetryFlow();
        var executor = new DslFlowExecutor<PaymentScenarioState, PaymentWithRetryFlow>(mediator, store, config);

        var state = new PaymentScenarioState
        {
            FlowId = "payment-retry",
            TransactionId = "TXN-002",
            Amount = 500m,
            Status = "Pending"
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.PaymentAttempts.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Payment_ExceedsLimit_ShouldRequireApproval()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new HighValuePaymentFlow();
        var executor = new DslFlowExecutor<PaymentScenarioState, HighValuePaymentFlow>(mediator, store, config);

        var state = new PaymentScenarioState
        {
            FlowId = "payment-high-value",
            TransactionId = "TXN-003",
            Amount = 100000m,
            Status = "Pending"
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.RequiresApproval.Should().BeTrue();
        state.Status.Should().Be("PendingApproval");
    }

    [Fact]
    public async Task Payment_Refund_ShouldReverseTransaction()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new RefundFlow();
        var executor = new DslFlowExecutor<PaymentScenarioState, RefundFlow>(mediator, store, config);

        var state = new PaymentScenarioState
        {
            FlowId = "payment-refund",
            TransactionId = "TXN-004",
            Amount = 1000m,
            Status = "Completed",
            IsRefund = true
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Status.Should().Be("Refunded");
        state.RefundAmount.Should().Be(1000m);
    }

    [Fact]
    public async Task Payment_PartialRefund_ShouldProcessCorrectly()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new PartialRefundFlow();
        var executor = new DslFlowExecutor<PaymentScenarioState, PartialRefundFlow>(mediator, store, config);

        var state = new PaymentScenarioState
        {
            FlowId = "payment-partial-refund",
            TransactionId = "TXN-005",
            Amount = 1000m,
            RefundAmount = 300m,
            Status = "Completed"
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Status.Should().Be("PartiallyRefunded");
    }

    [Fact]
    public async Task Payment_WithFraudCheck_ShouldValidate()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new FraudCheckPaymentFlow();
        var executor = new DslFlowExecutor<PaymentScenarioState, FraudCheckPaymentFlow>(mediator, store, config);

        var state = new PaymentScenarioState
        {
            FlowId = "payment-fraud-check",
            TransactionId = "TXN-006",
            Amount = 5000m,
            Status = "Pending",
            FraudScore = 0.3m
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.FraudCheckPassed.Should().BeTrue();
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(Task.CompletedTask);
    }
}

// Test state for payment scenarios
public class PaymentScenarioState : TestStateBase
{
    public string TransactionId { get; set; }
    public decimal Amount { get; set; }
    public decimal RefundAmount { get; set; }
    public string Status { get; set; } = "Pending";
    public int PaymentAttempts { get; set; }
    public bool RequiresApproval { get; set; }
    public bool IsRefund { get; set; }
    public decimal FraudScore { get; set; }
    public bool FraudCheckPassed { get; set; }
}

// Payment flow configurations
public class SuccessfulPaymentFlow : FlowConfig<PaymentScenarioState>
{
    protected override void Configure(IFlowBuilder<PaymentScenarioState> flow)
    {
        flow
            .Into(s => s.PaymentAttempts = 0)
            .Into(s => s.Status = "Processing")
            .Into(s => s.PaymentAttempts++)
            .Into(s => s.Status = "Completed");
    }
}

public class PaymentWithRetryFlow : FlowConfig<PaymentScenarioState>
{
    protected override void Configure(IFlowBuilder<PaymentScenarioState> flow)
    {
        flow
            .Into(s => s.PaymentAttempts = 0)
            .Repeat(3)
                .Into(s => s.PaymentAttempts++)
                .BreakIf(s => s.Status == "Completed")
            .EndRepeat()
            .Into(s => s.Status = "Completed");
    }
}

public class HighValuePaymentFlow : FlowConfig<PaymentScenarioState>
{
    protected override void Configure(IFlowBuilder<PaymentScenarioState> flow)
    {
        flow
            .When(s => s.Amount > 50000)
                .Into(s => s.RequiresApproval = true)
                .Into(s => s.Status = "PendingApproval")
            .EndWhen()
            .If(s => !s.RequiresApproval)
                .Into(s => s.Status = "Completed")
            .EndIf();
    }
}

public class RefundFlow : FlowConfig<PaymentScenarioState>
{
    protected override void Configure(IFlowBuilder<PaymentScenarioState> flow)
    {
        flow
            .If(s => s.IsRefund && s.Status == "Completed")
                .Into(s => s.RefundAmount = s.Amount)
                .Into(s => s.Status = "Refunded")
            .EndIf();
    }
}

public class PartialRefundFlow : FlowConfig<PaymentScenarioState>
{
    protected override void Configure(IFlowBuilder<PaymentScenarioState> flow)
    {
        flow
            .If(s => s.RefundAmount > 0 && s.RefundAmount < s.Amount)
                .Into(s => s.Status = "PartiallyRefunded")
            .ElseIf(s => s.RefundAmount == s.Amount)
                .Into(s => s.Status = "FullyRefunded")
            .EndIf();
    }
}

public class FraudCheckPaymentFlow : FlowConfig<PaymentScenarioState>
{
    protected override void Configure(IFlowBuilder<PaymentScenarioState> flow)
    {
        flow
            .When(s => s.FraudScore < 0.5m)
                .Into(s => s.FraudCheckPassed = true)
            .EndWhen()
            .If(s => s.FraudCheckPassed)
                .Into(s => s.Status = "Completed")
            .Else(f => f
                .Into(s => s.Status = "Blocked")
            )
            .EndIf();
    }
}
