using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Flow;

/// <summary>
/// End-to-end integration tests for real-world Flow scenarios.
/// </summary>
public class FlowE2EIntegrationTests
{
    #region Order Processing E2E

    [Fact]
    public async Task E2E_OrderProcessing_FullLifecycle()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new OrderProcessingFlow();
        var executor = new DslFlowExecutor<OrderState, OrderProcessingFlow>(mediator, store, config);

        SetupOrderMediatorSuccess(mediator);

        var state = new OrderState
        {
            FlowId = "order-e2e-001",
            OrderId = "ORD-12345",
            CustomerId = "CUST-001",
            Items = [
                new OrderItem("SKU-001", 2, 100m),
                new OrderItem("SKU-002", 1, 50m)
            ]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Status.Should().Be("Completed");
        result.State.TotalAmount.Should().Be(250m);
        result.State.PaymentId.Should().NotBeNullOrEmpty();
        result.State.ShipmentId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task E2E_OrderProcessing_PaymentFailure_Compensates()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new OrderProcessingFlow();
        var executor = new DslFlowExecutor<OrderState, OrderProcessingFlow>(mediator, store, config);

        SetupOrderMediatorWithPaymentFailure(mediator);

        var state = new OrderState
        {
            FlowId = "order-fail-001",
            OrderId = "ORD-FAIL",
            CustomerId = "CUST-001",
            Items = [new OrderItem("SKU-001", 1, 100m)]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.Status.Should().Be("PaymentFailed");
        result.State.CompensationExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_OrderProcessing_PartialShipment()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new PartialShipmentFlow();
        var executor = new DslFlowExecutor<OrderState, PartialShipmentFlow>(mediator, store, config);

        SetupPartialShipmentMediator(mediator);

        var state = new OrderState
        {
            FlowId = "partial-ship-001",
            OrderId = "ORD-PARTIAL",
            Items = [
                new OrderItem("SKU-AVAILABLE", 2, 50m),
                new OrderItem("SKU-BACKORDER", 1, 100m),
                new OrderItem("SKU-AVAILABLE-2", 3, 30m)
            ]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ShippedItems.Should().HaveCount(2);
        result.State.BackorderedItems.Should().HaveCount(1);
    }

    #endregion

    #region User Registration E2E

    [Fact]
    public async Task E2E_UserRegistration_Success()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new UserRegistrationFlow();
        var executor = new DslFlowExecutor<RegistrationState, UserRegistrationFlow>(mediator, store, config);

        SetupRegistrationMediatorSuccess(mediator);

        var state = new RegistrationState
        {
            FlowId = "reg-001",
            Email = "test@example.com",
            Username = "testuser"
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.UserId.Should().NotBeNullOrEmpty();
        result.State.EmailVerificationSent.Should().BeTrue();
        result.State.WelcomeEmailSent.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_UserRegistration_DuplicateEmail_Fails()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new UserRegistrationFlow();
        var executor = new DslFlowExecutor<RegistrationState, UserRegistrationFlow>(mediator, store, config);

        SetupRegistrationMediatorDuplicateEmail(mediator);

        var state = new RegistrationState
        {
            FlowId = "reg-dup-001",
            Email = "existing@example.com",
            Username = "newuser"
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("exists");
    }

    #endregion

    #region Data Pipeline E2E

    [Fact]
    public async Task E2E_DataPipeline_ProcessesAllRecords()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new DataPipelineFlow();
        var executor = new DslFlowExecutor<PipelineState, DataPipelineFlow>(mediator, store, config);

        SetupPipelineMediatorSuccess(mediator);

        var state = new PipelineState
        {
            FlowId = "pipeline-001",
            Records = Enumerable.Range(1, 100).Select(i => new DataRecord(i, $"Data-{i}")).ToList()
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedCount.Should().Be(100);
        result.State.FailedCount.Should().Be(0);
    }

    [Fact]
    public async Task E2E_DataPipeline_ContinuesOnErrors()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new DataPipelineWithErrorHandlingFlow();
        var executor = new DslFlowExecutor<PipelineState, DataPipelineWithErrorHandlingFlow>(mediator, store, config);

        SetupPipelineMediatorWithErrors(mediator);

        var state = new PipelineState
        {
            FlowId = "pipeline-errors-001",
            Records = Enumerable.Range(1, 50).Select(i => new DataRecord(i, $"Data-{i}")).ToList()
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedCount.Should().BeGreaterThan(0);
        result.State.FailedCount.Should().BeGreaterThan(0);
        (result.State.ProcessedCount + result.State.FailedCount).Should().Be(50);
    }

    #endregion

    #region Approval Workflow E2E

    [Fact]
    public async Task E2E_ApprovalWorkflow_AutoApproved_WhenUnderLimit()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ApprovalWorkflowFlow();
        var executor = new DslFlowExecutor<ApprovalState, ApprovalWorkflowFlow>(mediator, store, config);

        SetupApprovalMediatorSuccess(mediator);

        var state = new ApprovalState
        {
            FlowId = "approval-auto-001",
            RequestId = "REQ-001",
            Amount = 500m // Under auto-approval limit
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ApprovalStatus.Should().Be("AutoApproved");
        result.State.RequiredManualApproval.Should().BeFalse();
    }

    [Fact]
    public async Task E2E_ApprovalWorkflow_RequiresManagerApproval_WhenOverLimit()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ApprovalWorkflowFlow();
        var executor = new DslFlowExecutor<ApprovalState, ApprovalWorkflowFlow>(mediator, store, config);

        SetupApprovalMediatorSuccess(mediator);

        var state = new ApprovalState
        {
            FlowId = "approval-manual-001",
            RequestId = "REQ-002",
            Amount = 5000m // Over auto-approval limit
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ApprovalStatus.Should().Be("ManagerApproved");
        result.State.RequiredManualApproval.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_ApprovalWorkflow_RequiresExecutiveApproval_WhenVeryHigh()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ApprovalWorkflowFlow();
        var executor = new DslFlowExecutor<ApprovalState, ApprovalWorkflowFlow>(mediator, store, config);

        SetupApprovalMediatorSuccess(mediator);

        var state = new ApprovalState
        {
            FlowId = "approval-exec-001",
            RequestId = "REQ-003",
            Amount = 50000m // Requires executive approval
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ApprovalStatus.Should().Be("ExecutiveApproved");
        result.State.ApprovalChain.Should().Contain("Manager");
        result.State.ApprovalChain.Should().Contain("Executive");
    }

    #endregion

    #region Recovery and Persistence E2E

    [Fact]
    public async Task E2E_Recovery_ResumesFromCorrectStep()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new RecoverableOrderFlow();
        var executor = new DslFlowExecutor<OrderState, RecoverableOrderFlow>(mediator, store, config);

        var failOnFirst = true;
        mediator.SendAsync<ProcessPaymentRequest, string>(Arg.Any<ProcessPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (failOnFirst)
                {
                    failOnFirst = false;
                    return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Failure("Payment service unavailable"));
                }
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("PAY-123"));
            });

        SetupOrderMediatorSuccessExceptPayment(mediator);

        var state = new OrderState
        {
            FlowId = "recovery-001",
            OrderId = "ORD-RECOVER",
            Items = [new OrderItem("SKU-001", 1, 100m)]
        };

        // Act - First run fails
        var result1 = await executor.RunAsync(state);
        result1.IsSuccess.Should().BeFalse();
        result1.State.ValidationPassed.Should().BeTrue();

        // Act - Resume succeeds
        var result2 = await executor.ResumeAsync(state.FlowId!);

        // Assert
        result2.IsSuccess.Should().BeTrue();
        result2.State.PaymentId.Should().Be("PAY-123");
        result2.State.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task E2E_Persistence_StatePreservedAcrossSteps()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new PersistentStateFlow();
        var executor = new DslFlowExecutor<CounterState, PersistentStateFlow>(mediator, store, config);

        SetupCounterMediator(mediator);

        var state = new CounterState
        {
            FlowId = "persist-001",
            Values = [1, 2, 3, 4, 5]
        };

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Sum.Should().Be(15);
        result.State.ProcessedValues.Should().BeEquivalentTo([1, 2, 3, 4, 5]);

        // Verify state was persisted
        var savedSnapshot = await store.GetAsync<CounterState>("persist-001");
        savedSnapshot.Should().NotBeNull();
        savedSnapshot!.State.Sum.Should().Be(15);
    }

    #endregion

    #region Helper Methods

    private void SetupOrderMediatorSuccess(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        mediator.SendAsync<ValidateOrderRequest, bool>(Arg.Any<ValidateOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<CalculateTotalRequest, decimal>(Arg.Any<CalculateTotalRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var req = call.Arg<CalculateTotalRequest>();
                var total = req.Items.Sum(i => i.Quantity * i.Price);
                return new ValueTask<CatgaResult<decimal>>(CatgaResult<decimal>.Success(total));
            });

        mediator.SendAsync<ProcessPaymentRequest, string>(Arg.Any<ProcessPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("PAY-" + Guid.NewGuid().ToString("N")[..8])));

        mediator.SendAsync<CreateShipmentRequest, string>(Arg.Any<CreateShipmentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("SHIP-" + Guid.NewGuid().ToString("N")[..8])));
    }

    private void SetupOrderMediatorWithPaymentFailure(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        mediator.SendAsync<ValidateOrderRequest, bool>(Arg.Any<ValidateOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<CalculateTotalRequest, decimal>(Arg.Any<CalculateTotalRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<decimal>>(CatgaResult<decimal>.Success(100m)));

        mediator.SendAsync<ProcessPaymentRequest, string>(Arg.Any<ProcessPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Failure("Payment declined")));
    }

    private void SetupOrderMediatorSuccessExceptPayment(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        mediator.SendAsync<ValidateOrderRequest, bool>(Arg.Any<ValidateOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<CalculateTotalRequest, decimal>(Arg.Any<CalculateTotalRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<decimal>>(CatgaResult<decimal>.Success(100m)));

        mediator.SendAsync<CreateShipmentRequest, string>(Arg.Any<CreateShipmentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("SHIP-123")));
    }

    private void SetupPartialShipmentMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync<CheckInventoryRequest, bool>(Arg.Any<CheckInventoryRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var req = call.Arg<CheckInventoryRequest>();
                var available = !req.Sku.Contains("BACKORDER");
                return new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(available));
            });

        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));
    }

    private void SetupRegistrationMediatorSuccess(ICatgaMediator mediator)
    {
        mediator.SendAsync<CheckEmailExistsRequest, bool>(Arg.Any<CheckEmailExistsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(false)));

        mediator.SendAsync<CreateUserRequest, string>(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("USER-" + Guid.NewGuid().ToString("N")[..8])));

        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));
    }

    private void SetupRegistrationMediatorDuplicateEmail(ICatgaMediator mediator)
    {
        mediator.SendAsync<CheckEmailExistsRequest, bool>(Arg.Any<CheckEmailExistsRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Failure("Email already exists")));
    }

    private void SetupPipelineMediatorSuccess(ICatgaMediator mediator)
    {
        mediator.SendAsync<ProcessRecordRequest, bool>(Arg.Any<ProcessRecordRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));
    }

    private void SetupPipelineMediatorWithErrors(ICatgaMediator mediator)
    {
        var random = new Random(42);
        mediator.SendAsync<ProcessRecordRequest, bool>(Arg.Any<ProcessRecordRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (random.NextDouble() < 0.1) // 10% failure rate
                    return new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Failure("Random failure"));
                return new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true));
            });
    }

    private void SetupApprovalMediatorSuccess(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult>(CatgaResult.Success()));

        mediator.SendAsync<RequestApprovalRequest, bool>(Arg.Any<RequestApprovalRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));
    }

    private void SetupCounterMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync<AddValueRequest, int>(Arg.Any<AddValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var req = call.Arg<AddValueRequest>();
                return new ValueTask<CatgaResult<int>>(CatgaResult<int>.Success(req.Value));
            });
    }

    #endregion

    #region State Classes

    public class OrderState : BaseFlowState
    {
        public string? OrderId { get; set; }
        public string? CustomerId { get; set; }
        public List<OrderItem> Items { get; set; } = [];
        public decimal TotalAmount { get; set; }
        public string? PaymentId { get; set; }
        public string? ShipmentId { get; set; }
        public string Status { get; set; } = "Pending";
        public bool CompensationExecuted { get; set; }
        public bool ValidationPassed { get; set; }
        public List<string> ShippedItems { get; set; } = [];
        public List<string> BackorderedItems { get; set; } = [];
    }

    public record OrderItem(string Sku, int Quantity, decimal Price);

    public class RegistrationState : BaseFlowState
    {
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? UserId { get; set; }
        public bool EmailVerificationSent { get; set; }
        public bool WelcomeEmailSent { get; set; }
    }

    public class PipelineState : BaseFlowState
    {
        public List<DataRecord> Records { get; set; } = [];
        public int ProcessedCount { get; set; }
        public int FailedCount { get; set; }
    }

    public record DataRecord(int Id, string Data);

    public class ApprovalState : BaseFlowState
    {
        public string? RequestId { get; set; }
        public decimal Amount { get; set; }
        public string ApprovalStatus { get; set; } = "Pending";
        public bool RequiredManualApproval { get; set; }
        public List<string> ApprovalChain { get; set; } = [];
    }

    public class CounterState : BaseFlowState
    {
        public List<int> Values { get; set; } = [];
        public List<int> ProcessedValues { get; set; } = [];
        public int Sum { get; set; }
    }

    #endregion

    #region Request Classes

    public record ValidateOrderRequest(string OrderId) : IRequest<bool>;
    public record CalculateTotalRequest(List<OrderItem> Items) : IRequest<decimal>;
    public record ProcessPaymentRequest(string OrderId, decimal Amount) : IRequest<string>;
    public record CreateShipmentRequest(string OrderId) : IRequest<string>;
    public record CancelOrderRequest(string OrderId) : IRequest;
    public record CheckInventoryRequest(string Sku) : IRequest<bool>;
    public record ShipItemRequest(string Sku) : IRequest;
    public record BackorderItemRequest(string Sku) : IRequest;
    public record CheckEmailExistsRequest(string Email) : IRequest<bool>;
    public record CreateUserRequest(string Email, string Username) : IRequest<string>;
    public record SendVerificationEmailRequest(string Email) : IRequest;
    public record SendWelcomeEmailRequest(string UserId) : IRequest;
    public record ProcessRecordRequest(DataRecord Record) : IRequest<bool>;
    public record RequestApprovalRequest(string RequestId, string ApproverLevel) : IRequest<bool>;
    public record AddValueRequest(int Value) : IRequest<int>;

    #endregion

    #region Flow Configs

    public class OrderProcessingFlow : FlowConfig<OrderState>
    {
        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .Send(s => new ValidateOrderRequest(s.OrderId!))
                .Into((s, r) => s.ValidationPassed = r)
                .FailIf(r => !r, "Order validation failed")

                .Send(s => new CalculateTotalRequest(s.Items))
                .Into((s, r) => s.TotalAmount = r)

                .Send(s => new ProcessPaymentRequest(s.OrderId!, s.TotalAmount))
                .Into((s, r) => s.PaymentId = r)
                .IfFail(s => new CancelOrderRequest(s.OrderId!))

                .Send(s => new CreateShipmentRequest(s.OrderId!))
                .Into((s, r) =>
                {
                    s.ShipmentId = r;
                    s.Status = "Completed";
                });
        }
    }

    public class PartialShipmentFlow : FlowConfig<OrderState>
    {
        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow.ForEach(s => s.Items)
                .Send(item => new CheckInventoryRequest(item.Sku))
                .OnItemSuccess((s, item, result) =>
                {
                    if (result is true)
                        s.ShippedItems.Add(item.Sku);
                    else
                        s.BackorderedItems.Add(item.Sku);
                })
                .ContinueOnFailure()
                .EndForEach();
        }
    }

    public class UserRegistrationFlow : FlowConfig<RegistrationState>
    {
        protected override void Configure(IFlowBuilder<RegistrationState> flow)
        {
            flow
                .Send(s => new CheckEmailExistsRequest(s.Email!))
                .FailIf(r => r, "Email already exists")

                .Send(s => new CreateUserRequest(s.Email!, s.Username!))
                .Into((s, r) => s.UserId = r)

                .Send(s => new SendVerificationEmailRequest(s.Email!))
                .OnCompleted(s =>
                {
                    s.EmailVerificationSent = true;
                    return new UserRegisteredEvent(s.UserId!);
                })

                .Send(s => new SendWelcomeEmailRequest(s.UserId!))
                .OnCompleted(s =>
                {
                    s.WelcomeEmailSent = true;
                    return new WelcomeEmailSentEvent(s.UserId!);
                });
        }
    }

    public record UserRegisteredEvent(string UserId) : IEvent;
    public record WelcomeEmailSentEvent(string UserId) : IEvent;

    public class DataPipelineFlow : FlowConfig<PipelineState>
    {
        protected override void Configure(IFlowBuilder<PipelineState> flow)
        {
            flow.ForEach(s => s.Records)
                .Send(record => new ProcessRecordRequest(record))
                .OnItemSuccess((s, _, _) => s.ProcessedCount++)
                .WithParallelism(10)
                .EndForEach();
        }
    }

    public class DataPipelineWithErrorHandlingFlow : FlowConfig<PipelineState>
    {
        protected override void Configure(IFlowBuilder<PipelineState> flow)
        {
            flow.ForEach(s => s.Records)
                .Send(record => new ProcessRecordRequest(record))
                .OnItemSuccess((s, _, _) => s.ProcessedCount++)
                .OnItemFail((s, _, _) => s.FailedCount++)
                .ContinueOnFailure()
                .WithParallelism(10)
                .EndForEach();
        }
    }

    public class ApprovalWorkflowFlow : FlowConfig<ApprovalState>
    {
        protected override void Configure(IFlowBuilder<ApprovalState> flow)
        {
            flow
                .If(s => s.Amount <= 1000)
                    .Send(s => new RequestApprovalRequest(s.RequestId!, "Auto"))
                    .OnCompleted(s =>
                    {
                        s.ApprovalStatus = "AutoApproved";
                        return new ApprovalCompletedEvent(s.RequestId!);
                    })
                .ElseIf(s => s.Amount <= 10000)
                    .Send(s => new RequestApprovalRequest(s.RequestId!, "Manager"))
                    .OnCompleted(s =>
                    {
                        s.ApprovalStatus = "ManagerApproved";
                        s.RequiredManualApproval = true;
                        s.ApprovalChain.Add("Manager");
                        return new ApprovalCompletedEvent(s.RequestId!);
                    })
                .Else()
                    .Send(s => new RequestApprovalRequest(s.RequestId!, "Manager"))
                    .OnCompleted(s =>
                    {
                        s.ApprovalChain.Add("Manager");
                        return new ApprovalStepEvent(s.RequestId!, "Manager");
                    })
                    .Send(s => new RequestApprovalRequest(s.RequestId!, "Executive"))
                    .OnCompleted(s =>
                    {
                        s.ApprovalStatus = "ExecutiveApproved";
                        s.RequiredManualApproval = true;
                        s.ApprovalChain.Add("Executive");
                        return new ApprovalCompletedEvent(s.RequestId!);
                    })
                .EndIf();
        }
    }

    public record ApprovalCompletedEvent(string RequestId) : IEvent;
    public record ApprovalStepEvent(string RequestId, string Level) : IEvent;

    public class RecoverableOrderFlow : FlowConfig<OrderState>
    {
        protected override void Configure(IFlowBuilder<OrderState> flow)
        {
            flow
                .Send(s => new ValidateOrderRequest(s.OrderId!))
                .Into((s, r) => s.ValidationPassed = r)
                .Tag("persist")

                .Send(s => new ProcessPaymentRequest(s.OrderId!, 100m))
                .Into((s, r) => s.PaymentId = r)
                .Tag("persist")

                .Send(s => new CreateShipmentRequest(s.OrderId!))
                .Into((s, r) =>
                {
                    s.ShipmentId = r;
                    s.Status = "Completed";
                });
        }
    }

    public class PersistentStateFlow : FlowConfig<CounterState>
    {
        protected override void Configure(IFlowBuilder<CounterState> flow)
        {
            flow.ForEach(s => s.Values)
                .Send(value => new AddValueRequest(value))
                .OnItemSuccess((s, value, result) =>
                {
                    s.ProcessedValues.Add(value);
                    s.Sum += value;
                })
                .Tag("persist")
                .EndForEach();
        }
    }

    #endregion
}
