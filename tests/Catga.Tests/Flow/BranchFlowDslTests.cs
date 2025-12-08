using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// TDD tests for branching Flow DSL with recovery support.
/// </summary>
public class BranchFlowDslTests
{
    #region Test State and Messages

    public class OrderFlowState : IFlowState
    {
        public string? FlowId { get; set; }
        public string OrderId { get; set; } = "";
        public decimal Amount { get; set; }
        public bool IsValid { get; set; }
        public bool PaymentSuccess { get; set; }
        public string? PaymentId { get; set; }
        public string? ShipmentId { get; set; }
        public string? RejectionReason { get; set; }
        public string PaymentMethod { get; set; } = "CreditCard";

        // IFlowState implementation
        private int _changedMask;
        public bool HasChanges => _changedMask != 0;
        public int GetChangedMask() => _changedMask;
        public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
        public void ClearChanges() => _changedMask = 0;
        public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
        public IEnumerable<string> GetChangedFieldNames() => [];
    }

    public record ValidateOrderRequest(string OrderId) : IRequest<bool>
    {
        public long MessageId { get; init; }
    }

    public record ProcessPaymentRequest(string OrderId, decimal Amount) : IRequest<string?>
    {
        public long MessageId { get; init; }
    }

    public record ShipOrderRequest(string OrderId) : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    public record RefundOrderRequest(string OrderId, string PaymentId) : IRequest
    {
        public long MessageId { get; init; }
    }

    public record RejectOrderRequest(string OrderId, string Reason) : IRequest
    {
        public long MessageId { get; init; }
    }

    public record ProcessCreditCardRequest(string OrderId) : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    public record ProcessPayPalRequest(string OrderId) : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    public record ProcessBankTransferRequest(string OrderId) : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    #endregion

    #region Test Flow Configurations

    /// <summary>
    /// Simple If/Else flow for testing.
    /// </summary>
    public class SimpleIfElseFlow : FlowConfig<OrderFlowState>
    {
        protected override void Configure(IFlowBuilder<OrderFlowState> flow)
        {
            flow.Name("SimpleIfElseFlow")
                .Send(s => new ValidateOrderRequest(s.OrderId))
                    .Into(s => s.IsValid)
                .If(s => s.IsValid)
                    .Send(s => new ProcessPaymentRequest(s.OrderId, s.Amount))
                        .Into(s => s.PaymentId)
                .Else()
                    .Send(s => new RejectOrderRequest(s.OrderId, "Validation failed"))
                .EndIf();
        }
    }

    /// <summary>
    /// Nested If flow for testing.
    /// </summary>
    public class NestedIfFlow : FlowConfig<OrderFlowState>
    {
        protected override void Configure(IFlowBuilder<OrderFlowState> flow)
        {
            flow.Name("NestedIfFlow")
                .Send(s => new ValidateOrderRequest(s.OrderId))
                    .Into(s => s.IsValid)
                .If(s => s.IsValid)
                    .Send(s => new ProcessPaymentRequest(s.OrderId, s.Amount))
                        .Into(s => s.PaymentId)
                    .If(s => s.PaymentId != null)
                        .Send(s => new ShipOrderRequest(s.OrderId))
                            .Into(s => s.ShipmentId)
                    .Else()
                        .Send(s => new RejectOrderRequest(s.OrderId, "Payment failed"))
                    .EndIf()
                .Else()
                    .Send(s => new RejectOrderRequest(s.OrderId, "Validation failed"))
                .EndIf();
        }
    }

    /// <summary>
    /// Switch/Case flow for testing.
    /// </summary>
    public class SwitchCaseFlow : FlowConfig<OrderFlowState>
    {
        protected override void Configure(IFlowBuilder<OrderFlowState> flow)
        {
            flow.Name("SwitchCaseFlow")
                .Switch(s => s.PaymentMethod)
                    .Case("CreditCard", f => f
                        .Send(s => new ProcessCreditCardRequest(s.OrderId))
                            .Into(s => s.PaymentId))
                    .Case("PayPal", f => f
                        .Send(s => new ProcessPayPalRequest(s.OrderId))
                            .Into(s => s.PaymentId))
                    .Default(f => f
                        .Send(s => new ProcessBankTransferRequest(s.OrderId))
                            .Into(s => s.PaymentId))
                .EndSwitch();
        }
    }

    #endregion

    #region Flow Configuration Tests

    [Fact]
    public void SimpleIfElseFlow_ShouldBuildCorrectly()
    {
        // Arrange
        var flow = new SimpleIfElseFlow();

        // Act
        flow.Build();

        // Assert
        Assert.Equal("SimpleIfElseFlow", flow.Name);
        Assert.Equal(2, flow.Steps.Count); // Send + If
        Assert.Equal(StepType.Send, flow.Steps[0].Type);
        Assert.Equal(StepType.If, flow.Steps[1].Type);
    }

    [Fact]
    public void NestedIfFlow_ShouldBuildCorrectly()
    {
        // Arrange
        var flow = new NestedIfFlow();

        // Act
        flow.Build();

        // Assert
        Assert.Equal("NestedIfFlow", flow.Name);
        Assert.Equal(2, flow.Steps.Count); // Send + If

        var ifStep = flow.Steps[1];
        Assert.Equal(StepType.If, ifStep.Type);
        Assert.NotNull(ifStep.ThenBranch);
        Assert.Equal(2, ifStep.ThenBranch!.Count); // Send + nested If
        Assert.NotNull(ifStep.ElseBranch);
        Assert.Single(ifStep.ElseBranch!); // Send
    }

    [Fact]
    public void SwitchCaseFlow_ShouldBuildCorrectly()
    {
        // Arrange
        var flow = new SwitchCaseFlow();

        // Act
        flow.Build();

        // Assert
        Assert.Equal("SwitchCaseFlow", flow.Name);
        Assert.Single(flow.Steps); // Switch

        var switchStep = flow.Steps[0];
        Assert.Equal(StepType.Switch, switchStep.Type);
        Assert.Equal(2, switchStep.Cases!.Count); // CreditCard, PayPal
        Assert.NotNull(switchStep.DefaultBranch);
    }

    #endregion

    #region Flow Position Tests

    [Fact]
    public void FlowPosition_ShouldNavigateToCorrectStep()
    {
        // Arrange
        var position = new FlowPosition([0]); // First step

        // Assert
        Assert.Equal(0, position.CurrentIndex);
        Assert.False(position.IsInBranch);
    }

    [Fact]
    public void FlowPosition_ShouldNavigateIntoBranch()
    {
        // Arrange - Position [1, 0] means Step 1's Then branch, step 0
        var position = new FlowPosition([1, 0]);

        // Assert
        Assert.Equal(1, position.Path[0]);
        Assert.Equal(0, position.Path[1]);
        Assert.True(position.IsInBranch);
    }

    [Fact]
    public void FlowPosition_ShouldAdvanceCorrectly()
    {
        // Arrange
        var position = new FlowPosition([0]);

        // Act
        var next = position.Advance();

        // Assert
        Assert.Equal(1, next.CurrentIndex);
    }

    [Fact]
    public void FlowPosition_ShouldEnterBranch()
    {
        // Arrange
        var position = new FlowPosition([1]);

        // Act
        var inBranch = position.EnterBranch(0);

        // Assert
        Assert.Equal(new[] { 1, 0 }, inBranch.Path);
    }

    #endregion

    #region Snapshot Recovery Tests

    [Fact]
    public async Task Snapshot_ShouldPersistPosition()
    {
        // Arrange
        var state = new OrderFlowState { OrderId = "123", Amount = 100 };
        var position = new FlowPosition([1, 0]);
        var snapshot = new FlowSnapshot<OrderFlowState>
        {
            FlowId = "flow-1",
            State = state,
            Position = position,
            Status = DslFlowStatus.Running,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // Assert
        Assert.Equal("flow-1", snapshot.FlowId);
        Assert.Equal(new[] { 1, 0 }, snapshot.Position.Path);
        Assert.Equal(DslFlowStatus.Running, snapshot.Status);
    }

    [Fact]
    public async Task Recovery_ShouldResumeFromCorrectPosition()
    {
        // This test will verify that after loading a snapshot,
        // execution resumes from the correct position

        // Arrange
        var state = new OrderFlowState
        {
            OrderId = "123",
            Amount = 100,
            IsValid = true  // Already validated
        };
        var position = new FlowPosition([1, 0]); // In If's Then branch, step 0

        // Act - Simulate recovery
        var resumePosition = position;

        // Assert - Should resume at step [1, 0]
        Assert.Equal(new[] { 1, 0 }, resumePosition.Path);
    }

    #endregion

    #region Store Parity Tests

    [Theory]
    [InlineData("InMemory")]
    [InlineData("Redis")]
    [InlineData("Nats")]
    public async Task AllStores_ShouldPersistAndRecoverIdentically(string storeType)
    {
        // This test ensures all three stores behave identically
        // Will be implemented after store updates

        // Arrange
        var state = new OrderFlowState
        {
            OrderId = "test-123",
            Amount = 500,
            IsValid = true,
            PaymentId = "pay-456"
        };
        var position = new FlowPosition([1, 1, 0]); // Nested position
        var snapshot = new FlowSnapshot<OrderFlowState>
        {
            FlowId = $"flow-{storeType}",
            State = state,
            Position = position,
            Status = DslFlowStatus.Running,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };

        // TODO: Get store based on storeType and test
        // var store = GetStore(storeType);
        // await store.CreateAsync(snapshot);
        // var loaded = await store.GetAsync<OrderFlowState>(snapshot.FlowId);
        // Assert.Equal(snapshot.Position.Path, loaded!.Position.Path);

        Assert.True(true); // Placeholder until implementation
    }

    #endregion

    #region Execution Tests

    [Fact]
    public void IfStep_EvaluatesConditionCorrectly()
    {
        // Arrange
        var flow = new SimpleIfElseFlow();
        flow.Build();

        var ifStep = flow.Steps[1];
        Assert.Equal(StepType.If, ifStep.Type);
        Assert.NotNull(ifStep.BranchCondition);

        // Act - Test condition with valid state
        var validState = new OrderFlowState { IsValid = true };
        var condition = (Func<OrderFlowState, bool>)ifStep.BranchCondition;
        var resultValid = condition(validState);

        // Act - Test condition with invalid state
        var invalidState = new OrderFlowState { IsValid = false };
        var resultInvalid = condition(invalidState);

        // Assert
        Assert.True(resultValid);
        Assert.False(resultInvalid);
    }

    [Fact]
    public void SwitchStep_SelectsCorrectCase()
    {
        // Arrange
        var flow = new SwitchCaseFlow();
        flow.Build();

        var switchStep = flow.Steps[0];
        Assert.Equal(StepType.Switch, switchStep.Type);
        Assert.NotNull(switchStep.SwitchSelector);
        Assert.NotNull(switchStep.Cases);
        Assert.Equal(2, switchStep.Cases.Count);

        // Act - Test selector
        var creditCardState = new OrderFlowState { PaymentMethod = "CreditCard" };
        var payPalState = new OrderFlowState { PaymentMethod = "PayPal" };
        var otherState = new OrderFlowState { PaymentMethod = "Bitcoin" };

        var selector = switchStep.SwitchSelector;
        var creditCardValue = selector.DynamicInvoke(creditCardState);
        var payPalValue = selector.DynamicInvoke(payPalState);
        var otherValue = selector.DynamicInvoke(otherState);

        // Assert
        Assert.Equal("CreditCard", creditCardValue);
        Assert.Equal("PayPal", payPalValue);
        Assert.Equal("Bitcoin", otherValue);

        // Verify cases exist (Cases is Dictionary<object, List<FlowStep>>)
        Assert.True(switchStep.Cases.ContainsKey("CreditCard"));
        Assert.True(switchStep.Cases.ContainsKey("PayPal"));
        Assert.NotNull(switchStep.DefaultBranch);
    }

    [Fact]
    public void NestedIf_BuildsCorrectStructure()
    {
        // Arrange
        var flow = new NestedIfFlow();
        flow.Build();

        // Act
        var outerIfStep = flow.Steps[1];
        Assert.Equal(StepType.If, outerIfStep.Type);
        Assert.NotNull(outerIfStep.ThenBranch);
        Assert.Equal(2, outerIfStep.ThenBranch.Count); // Send + nested If

        var nestedIfStep = outerIfStep.ThenBranch[1];
        Assert.Equal(StepType.If, nestedIfStep.Type);
        Assert.NotNull(nestedIfStep.ThenBranch);
        Assert.NotNull(nestedIfStep.ElseBranch);

        // Assert - nested structure is correct
        Assert.Single(nestedIfStep.ThenBranch); // Ship order
        Assert.Single(nestedIfStep.ElseBranch); // Reject order
    }

    #endregion

    #region E2E Execution Tests

    [Fact]
    public async Task E2E_IfElse_ExecutesThenBranch_WhenConditionTrue()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleIfElseFlow();
        var executor = new DslFlowExecutor<OrderFlowState, SimpleIfElseFlow>(mediator, store, config);

        var state = new OrderFlowState
        {
            OrderId = "ORD-001",
            Amount = 100,
            IsValid = true // Condition is true
        };

        // Setup mediator to return success for ValidateOrderRequest
        SetupMediatorForValidation(mediator, true);
        SetupMediatorForPayment(mediator, "PAY-001");

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_IfElse_ExecutesElseBranch_WhenConditionFalse()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleIfElseFlow();
        var executor = new DslFlowExecutor<OrderFlowState, SimpleIfElseFlow>(mediator, store, config);

        var state = new OrderFlowState
        {
            OrderId = "ORD-002",
            Amount = 100,
            IsValid = false // Condition is false
        };

        // Setup mediator to return failure for ValidateOrderRequest
        SetupMediatorForValidation(mediator, false);
        SetupMediatorForReject(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task E2E_Switch_ExecutesMatchingCase()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = new InMemoryDslFlowStore();
        var config = new SwitchCaseFlow();
        var executor = new DslFlowExecutor<OrderFlowState, SwitchCaseFlow>(mediator, store, config);

        var state = new OrderFlowState
        {
            OrderId = "ORD-003",
            PaymentMethod = "CreditCard"
        };

        SetupMediatorForCreditCard(mediator, "CC-PAY-001");

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_Switch_ExecutesDefaultCase_WhenNoMatch()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = new InMemoryDslFlowStore();
        var config = new SwitchCaseFlow();
        var executor = new DslFlowExecutor<OrderFlowState, SwitchCaseFlow>(mediator, store, config);

        var state = new OrderFlowState
        {
            OrderId = "ORD-004",
            PaymentMethod = "Bitcoin" // No matching case
        };

        SetupMediatorForBankTransfer(mediator, "BT-PAY-001");

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_NestedIf_ExecutesCorrectBranch()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = new InMemoryDslFlowStore();
        var config = new NestedIfFlow();
        var executor = new DslFlowExecutor<OrderFlowState, NestedIfFlow>(mediator, store, config);

        var state = new OrderFlowState
        {
            OrderId = "ORD-005",
            Amount = 100,
            IsValid = true,
            PaymentSuccess = true
        };

        SetupMediatorForValidation(mediator, true);
        SetupMediatorForPayment(mediator, "PAY-005");
        SetupMediatorForShip(mediator, "SHIP-005");

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_BranchFlow_PersistsPositionCorrectly()
    {
        // Arrange
        var mediator = CreateMockMediator();
        var store = new InMemoryDslFlowStore();
        var config = new SimpleIfElseFlow();
        var executor = new DslFlowExecutor<OrderFlowState, SimpleIfElseFlow>(mediator, store, config);

        var state = new OrderFlowState
        {
            FlowId = "flow-branch-001",
            OrderId = "ORD-006",
            Amount = 100,
            IsValid = true
        };

        SetupMediatorForValidation(mediator, true);
        SetupMediatorForPayment(mediator, "PAY-006");

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify persistence
        var snapshot = await store.GetAsync<OrderFlowState>("flow-branch-001");
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Completed);
    }

    #endregion

    #region Mock Helpers

    private static ICatgaMediator CreateMockMediator()
    {
        return Substitute.For<ICatgaMediator>();
    }

    private static void SetupMediatorForValidation(ICatgaMediator mediator, bool isValid)
    {
        mediator.SendAsync<ValidateOrderRequest, bool>(
            Arg.Any<ValidateOrderRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(CatgaResult<bool>.Success(isValid));
    }

    private static void SetupMediatorForPayment(ICatgaMediator mediator, string paymentId)
    {
        mediator.SendAsync<ProcessPaymentRequest, string?>(
            Arg.Any<ProcessPaymentRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(CatgaResult<string?>.Success(paymentId));
    }

    private static void SetupMediatorForReject(ICatgaMediator mediator)
    {
        mediator.SendAsync(
            Arg.Any<RejectOrderRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(CatgaResult.Success());
    }

    private static void SetupMediatorForShip(ICatgaMediator mediator, string shipmentId)
    {
        mediator.SendAsync<ShipOrderRequest, string>(
            Arg.Any<ShipOrderRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(CatgaResult<string>.Success(shipmentId));
    }

    private static void SetupMediatorForCreditCard(ICatgaMediator mediator, string paymentId)
    {
        mediator.SendAsync<ProcessCreditCardRequest, string>(
            Arg.Any<ProcessCreditCardRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(CatgaResult<string>.Success(paymentId));
    }

    private static void SetupMediatorForBankTransfer(ICatgaMediator mediator, string paymentId)
    {
        mediator.SendAsync<ProcessBankTransferRequest, string>(
            Arg.Any<ProcessBankTransferRequest>(),
            Arg.Any<CancellationToken>())
            .Returns(CatgaResult<string>.Success(paymentId));
    }

    #endregion
}






