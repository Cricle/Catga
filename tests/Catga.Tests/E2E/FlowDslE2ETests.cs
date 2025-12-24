using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Flow.Dsl;
using Catga.Flow.Extensions;
using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// End-to-end tests for Flow DSL with source-generated registration.
/// Tests complete scenarios from registration to execution.
/// </summary>
public class FlowDslE2ETests
{
    [Fact]
    public async Task E2E_CompleteOrderProcessingFlow()
    {
        // Arrange - Setup DI container with all dependencies
        var services = new ServiceCollection();
        var mediator = SetupMediatorForOrderFlow();

        services.AddSingleton(mediator);
        services.AddSingleton<IMessageSerializer, TestMessageSerializer>();

        // Use fluent configuration with source-generated registration
        services.ConfigureFlowDsl(flow => flow
            .UseInMemoryStorage()
            .RegisterFlow<OrderE2EState, OrderProcessingE2EFlow>()
            .WithRetryPolicy(3)
            .WithStepTimeout(TimeSpan.FromSeconds(30)));

        var provider = services.BuildServiceProvider();

        // Act - Execute the complete order flow
        var executor = provider.GetService<DslFlowExecutor<OrderE2EState, OrderProcessingE2EFlow>>();

        var orderState = new OrderE2EState
        {
            FlowId = "order-e2e-001",
            OrderId = "ORD-2024-001",
            CustomerId = "CUST-123",
            Items = new List<OrderItemE2E>
            {
                new() { ProductId = "PROD-001", Quantity = 2, Price = 50.00m },
                new() { ProductId = "PROD-002", Quantity = 1, Price = 100.00m },
                new() { ProductId = "PROD-003", Quantity = 3, Price = 25.00m }
            }
        };

        var result = await executor!.RunAsync(orderState);

        // Assert - Verify complete flow execution
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();

        var finalState = result.State;

        // Inventory should be checked
        finalState.InventoryChecked.Should().BeTrue();
        finalState.AllItemsAvailable.Should().BeTrue();

        // Payment should be processed
        finalState.PaymentProcessed.Should().BeTrue();
        finalState.PaymentTransactionId.Should().NotBeNullOrWhiteSpace();

        // Order should be confirmed
        finalState.OrderConfirmed.Should().BeTrue();
        finalState.ConfirmationNumber.Should().NotBeNullOrWhiteSpace();

        // Shipping should be scheduled
        finalState.ShippingScheduled.Should().BeTrue();
        finalState.TrackingNumber.Should().NotBeNullOrWhiteSpace();

        // Email should be sent
        finalState.EmailSent.Should().BeTrue();

        // Total amount should be calculated
        finalState.TotalAmount.Should().Be(275.00m); // (2*50) + (1*100) + (3*25)
    }

    [Fact]
    public async Task E2E_ConditionalFlowWithBranching()
    {
        // Arrange
        var services = new ServiceCollection();
        var mediator = SetupMediatorForConditionalFlow();

        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<ConditionalE2EState, ConditionalE2EFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ConditionalE2EState, ConditionalE2EFlow>>();

        // Test Case 1: VIP Customer
        var vipState = new ConditionalE2EState
        {
            FlowId = "conditional-vip-001",
            CustomerType = "VIP",
            OrderAmount = 1500.00m
        };

        var vipResult = await executor!.RunAsync(vipState);

        // Assert VIP processing
        vipResult.IsSuccess.Should().BeTrue();
        vipResult.State.DiscountApplied.Should().Be(0.20m); // 20% VIP discount
        vipResult.State.ShippingType.Should().Be("Express");
        vipResult.State.LoyaltyPointsAwarded.Should().Be(300); // Double points

        // Test Case 2: Regular Customer
        var regularState = new ConditionalE2EState
        {
            FlowId = "conditional-regular-001",
            CustomerType = "Regular",
            OrderAmount = 500.00m
        };

        var regularResult = await executor.RunAsync(regularState);

        // Assert regular processing
        regularResult.IsSuccess.Should().BeTrue();
        regularResult.State.DiscountApplied.Should().Be(0.05m); // 5% regular discount
        regularResult.State.ShippingType.Should().Be("Standard");
        regularResult.State.LoyaltyPointsAwarded.Should().Be(50);

        // Test Case 3: New Customer
        var newState = new ConditionalE2EState
        {
            FlowId = "conditional-new-001",
            CustomerType = "New",
            OrderAmount = 200.00m
        };

        var newResult = await executor.RunAsync(newState);

        // Assert new customer processing
        newResult.IsSuccess.Should().BeTrue();
        newResult.State.DiscountApplied.Should().Be(0.10m); // 10% welcome discount
        newResult.State.WelcomeEmailSent.Should().BeTrue();
        newResult.State.LoyaltyPointsAwarded.Should().Be(40); // Bonus points
    }

    [Fact]
    public async Task E2E_ParallelProcessingWithForEach()
    {
        // Arrange
        var services = new ServiceCollection();
        var mediator = SetupMediatorForParallelProcessing();

        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<ParallelE2EState, ParallelProcessingE2EFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ParallelE2EState, ParallelProcessingE2EFlow>>();

        // Create test data with 100 items
        var items = Enumerable.Range(1, 100)
            .Select(i => new ProcessingItemE2E { Id = $"ITEM-{i:D3}", Value = i })
            .ToList();

        var state = new ParallelE2EState
        {
            FlowId = "parallel-e2e-001",
            Items = items
        };

        var startTime = DateTime.UtcNow;

        // Act
        var result = await executor!.RunAsync(state);

        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;

        // Assert
        result.IsSuccess.Should().BeTrue();

        // All items should be processed
        result.State.ProcessedItems.Count.Should().Be(100);
        result.State.FailedItems.Should().BeEmpty();

        // Results should be collected
        result.State.Results.Count.Should().Be(100);
        result.State.Results.Values.Should().AllSatisfy(r => r.Should().BeGreaterThan(0));

        // Parallel processing should be faster than sequential
        // With parallelism of 10, processing 100 items with 50ms delay each
        // Sequential would take 5000ms, parallel should take ~500ms
        duration.TotalMilliseconds.Should().BeLessThan(2000, "Parallel processing should be fast");
    }

    [Fact]
    public async Task E2E_FlowRecoveryAfterFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        var mediator = SetupMediatorForRecovery();
        var store = TestStoreExtensions.CreateTestFlowStore();

        services.AddSingleton(mediator);
        services.AddSingleton<IDslFlowStore>(store);
        services.AddFlow<RecoveryE2EState, RecoveryE2EFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<RecoveryE2EState, RecoveryE2EFlow>>();

        var state = new RecoveryE2EState
        {
            FlowId = "recovery-e2e-001",
            Steps = new List<string> { "Step1", "Step2", "FailStep", "Step3", "Step4" }
        };

        // Act - First run (will fail at FailStep)
        var firstRun = await executor!.RunAsync(state);

        firstRun.IsSuccess.Should().BeFalse();
        firstRun.State.CompletedSteps.Should().BeEquivalentTo(new[] { "Step1", "Step2" });

        // Get snapshot from store
        var snapshot = await store.GetAsync<RecoveryE2EState>(state.FlowId!);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be(DslFlowStatus.Failed);

        // Fix the issue (change mediator behavior)
        SetupMediatorForRecoverySuccess(mediator);

        // Act - Resume flow
        var resumeResult = await executor.ResumeAsync(state.FlowId!);

        // Assert - Flow should complete successfully
        resumeResult.IsSuccess.Should().BeTrue();
        resumeResult.State.CompletedSteps.Should().BeEquivalentTo(
            new[] { "Step1", "Step2", "FailStep", "Step3", "Step4" });
        resumeResult.State.AllStepsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_WhenAllCoordination()
    {
        // Arrange
        var services = new ServiceCollection();
        var mediator = SetupMediatorForCoordination();

        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<CoordinationE2EState, CoordinationE2EFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<CoordinationE2EState, CoordinationE2EFlow>>();

        var state = new CoordinationE2EState
        {
            FlowId = "coordination-e2e-001",
            OrderId = "ORD-COORD-001"
        };

        // Act
        var result = await executor!.RunAsync(state);

        // Assert - All parallel operations should complete
        result.IsSuccess.Should().BeTrue();

        result.State.InvoiceGenerated.Should().BeTrue();
        result.State.InvoiceNumber.Should().NotBeNullOrWhiteSpace();

        result.State.InventoryUpdated.Should().BeTrue();

        result.State.CustomerNotified.Should().BeTrue();

        result.State.ShippingArranged.Should().BeTrue();
        result.State.ShippingCarrier.Should().NotBeNullOrWhiteSpace();

        result.State.AllOperationsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_WhenAnyRaceCondition()
    {
        // Arrange
        var services = new ServiceCollection();
        var mediator = SetupMediatorForRacing();

        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<RaceE2EState, RaceConditionE2EFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<RaceE2EState, RaceConditionE2EFlow>>();

        var state = new RaceE2EState
        {
            FlowId = "race-e2e-001",
            Amount = 1000.00m
        };

        // Act
        var result = await executor!.RunAsync(state);

        // Assert - One provider should win
        result.IsSuccess.Should().BeTrue();

        result.State.PaymentProcessed.Should().BeTrue();
        result.State.WinningProvider.Should().BeOneOf("Provider1", "Provider2", "Provider3");
        result.State.TransactionId.Should().NotBeNullOrWhiteSpace();
        result.State.ProcessingTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    // Helper methods for setting up mediator mocks
    private ICatgaMediator SetupMediatorForOrderFlow()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<CheckInventoryCommand, bool>(Arg.Any<CheckInventoryCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<ProcessPaymentCommand, string>(Arg.Any<ProcessPaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("TXN-12345")));

        mediator.SendAsync<ConfirmOrderCommand, string>(Arg.Any<ConfirmOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("CONF-98765")));

        mediator.SendAsync<ScheduleShippingCommand, string>(Arg.Any<ScheduleShippingCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("TRACK-54321")));

        mediator.SendAsync<SendEmailCommand, bool>(Arg.Any<SendEmailCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    private ICatgaMediator SetupMediatorForConditionalFlow()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ApplyDiscountCommand, decimal>(Arg.Any<ApplyDiscountCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<ApplyDiscountCommand>();
                return new ValueTask<CatgaResult<decimal>>(CatgaResult<decimal>.Success(cmd.Percentage));
            });

        mediator.SendAsync<SetShippingCommand, string>(Arg.Any<SetShippingCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<SetShippingCommand>();
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success(cmd.Type));
            });

        mediator.SendAsync<AwardPointsCommand, int>(Arg.Any<AwardPointsCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<AwardPointsCommand>();
                return new ValueTask<CatgaResult<int>>(CatgaResult<int>.Success(cmd.Points));
            });

        mediator.SendAsync<SendWelcomeCommand, bool>(Arg.Any<SendWelcomeCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    private ICatgaMediator SetupMediatorForParallelProcessing()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ProcessItemCommand, int>(Arg.Any<ProcessItemCommand>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var cmd = call.Arg<ProcessItemCommand>();
                await Task.Delay(50); // Simulate processing time
                return CatgaResult<int>.Success(cmd.Value * 2);
            });

        return mediator;
    }

    private ICatgaMediator SetupMediatorForRecovery()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ExecuteStepCommand, bool>(Arg.Any<ExecuteStepCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var cmd = call.Arg<ExecuteStepCommand>();
                if (cmd.StepName == "FailStep")
                {
                    return new ValueTask<CatgaResult<bool>>(
                        CatgaResult<bool>.Failure("Step failed intentionally"));
                }
                return new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true));
            });

        return mediator;
    }

    private void SetupMediatorForRecoverySuccess(ICatgaMediator mediator)
    {
        mediator.SendAsync<ExecuteStepCommand, bool>(Arg.Any<ExecuteStepCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));
    }

    private ICatgaMediator SetupMediatorForCoordination()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<GenerateInvoiceCommand, string>(Arg.Any<GenerateInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(100);
                return CatgaResult<string>.Success("INV-2024-001");
            });

        mediator.SendAsync<UpdateInventoryCommand, bool>(Arg.Any<UpdateInventoryCommand>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(150);
                return CatgaResult<bool>.Success(true);
            });

        mediator.SendAsync<NotifyCustomerCommand, bool>(Arg.Any<NotifyCustomerCommand>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(50);
                return CatgaResult<bool>.Success(true);
            });

        mediator.SendAsync<ArrangeShippingCommand, string>(Arg.Any<ArrangeShippingCommand>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(200);
                return CatgaResult<string>.Success("FedEx");
            });

        return mediator;
    }

    private ICatgaMediator SetupMediatorForRacing()
    {
        var mediator = Substitute.For<ICatgaMediator>();
        var random = new Random();

        mediator.SendAsync<PaymentProviderCommand, PaymentResult>(Arg.Any<PaymentProviderCommand>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var cmd = call.Arg<PaymentProviderCommand>();
                var delay = random.Next(100, 500); // Random delay to simulate racing
                await Task.Delay(delay);

                return CatgaResult<PaymentResult>.Success(new PaymentResult
                {
                    Provider = cmd.Provider,
                    TransactionId = $"TXN-{cmd.Provider}-{Guid.NewGuid():N}".Substring(0, 20),
                    ProcessingTime = TimeSpan.FromMilliseconds(delay)
                });
            });

        return mediator;
    }
}

// E2E Flow Configurations and States
// ... (The rest would contain all the flow configurations, states, and commands used in the tests)
// Due to length constraints, I'll create these in a separate file
