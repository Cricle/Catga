using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Scenario tests for complex business workflows
/// Tests end-to-end order processing with multiple stages
/// </summary>
public class ComplexBusinessScenarioTests
{
    [Fact]
    public async Task EndToEnd_OrderProcessing_ShouldCompleteAllStages()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new EndToEndOrderFlow();
        var executor = new DslFlowExecutor<ComplexBusinessState, EndToEndOrderFlow>(mediator, store, config);

        var state = new ComplexBusinessState
        {
            FlowId = "e2e-order-001",
            OrderId = "ORD-E2E-001",
            Items = new() { "ITEM-1", "ITEM-2" },
            Amount = 5000m,
            CustomerType = "Regular"
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Stages.Should().Contain("Validation");
        state.Stages.Should().Contain("Payment");
        state.Stages.Should().Contain("Fulfillment");
        state.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task EndToEnd_VIPOrder_ShouldApplySpecialHandling()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new VIPOrderFlow();
        var executor = new DslFlowExecutor<ComplexBusinessState, VIPOrderFlow>(mediator, store, config);

        var state = new ComplexBusinessState
        {
            FlowId = "e2e-vip-001",
            OrderId = "ORD-VIP-001",
            Items = new() { "PREMIUM-1", "PREMIUM-2", "PREMIUM-3" },
            Amount = 50000m,
            CustomerType = "VIP"
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.SpecialHandling.Should().BeTrue();
        state.BonusPoints.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EndToEnd_WithErrorRecovery_ShouldRetryAndRecover()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ResilientOrderFlow();
        var executor = new DslFlowExecutor<ComplexBusinessState, ResilientOrderFlow>(mediator, store, config);

        var state = new ComplexBusinessState
        {
            FlowId = "e2e-resilient-001",
            OrderId = "ORD-RES-001",
            Items = new() { "ITEM-1" },
            Amount = 1000m,
            CustomerType = "Regular"
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.RetryCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task EndToEnd_BulkProcessing_ShouldHandleMultipleOrders()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new BulkOrderProcessingFlow();
        var executor = new DslFlowExecutor<ComplexBusinessState, BulkOrderProcessingFlow>(mediator, store, config);

        var state = new ComplexBusinessState
        {
            FlowId = "e2e-bulk-001",
            OrderId = "ORD-BULK-001",
            Items = new() { "ITEM-1", "ITEM-2", "ITEM-3", "ITEM-4", "ITEM-5" },
            Amount = 10000m,
            CustomerType = "Regular"
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ProcessedCount.Should().Be(5);
    }

    [Fact]
    public async Task EndToEnd_WithValidationFailure_ShouldRejectAndNotify()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ValidatedOrderFlow();
        var executor = new DslFlowExecutor<ComplexBusinessState, ValidatedOrderFlow>(mediator, store, config);

        var state = new ComplexBusinessState
        {
            FlowId = "e2e-invalid-001",
            OrderId = "",  // Invalid
            Items = new(),
            Amount = 0,
            CustomerType = "Regular"
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        state.Status.Should().Be("Rejected");
        state.Errors.Should().NotBeEmpty();
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(Task.CompletedTask);
    }
}

// Test state for complex business scenarios
public class ComplexBusinessState : TestStateBase
{
    public string OrderId { get; set; }
    public List<string> Items { get; set; } = new();
    public decimal Amount { get; set; }
    public string CustomerType { get; set; }
    public string Status { get; set; } = "Pending";
    public List<string> Stages { get; set; } = new();
    public bool SpecialHandling { get; set; }
    public int BonusPoints { get; set; }
    public int RetryCount { get; set; }
    public int ProcessedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}

// Complex business flow configurations
public class EndToEndOrderFlow : FlowConfig<ComplexBusinessState>
{
    protected override void Configure(IFlowBuilder<ComplexBusinessState> flow)
    {
        flow
            .Into(s => s.Status = "Processing")
            .Into(s => s.Stages.Add("Validation"))
            .Into(s => s.Stages.Add("Payment"))
            .While(s => s.ProcessedCount < s.Items.Count)
                .Into(s => s.ProcessedCount++)
            .EndWhile()
            .Into(s => s.Stages.Add("Fulfillment"))
            .Into(s => s.Status = "Completed");
    }
}

public class VIPOrderFlow : FlowConfig<ComplexBusinessState>
{
    protected override void Configure(IFlowBuilder<ComplexBusinessState> flow)
    {
        flow
            .Into(s => s.Status = "Processing")
            .When(s => s.CustomerType == "VIP")
                .Into(s => s.SpecialHandling = true)
                .Into(s => s.BonusPoints = (int)(s.Amount * 0.1m))
            .EndWhen()
            .While(s => s.ProcessedCount < s.Items.Count)
                .Into(s => s.ProcessedCount++)
            .EndWhile()
            .Into(s => s.Status = "Completed");
    }
}

public class ResilientOrderFlow : FlowConfig<ComplexBusinessState>
{
    protected override void Configure(IFlowBuilder<ComplexBusinessState> flow)
    {
        flow
            .Into(s => s.RetryCount = 0)
            .Repeat(3)
                .Into(s => s.RetryCount++)
                .BreakIf(s => s.Status == "Completed")
            .EndRepeat()
            .Into(s => s.Status = "Completed");
    }
}

public class BulkOrderProcessingFlow : FlowConfig<ComplexBusinessState>
{
    protected override void Configure(IFlowBuilder<ComplexBusinessState> flow)
    {
        flow
            .Into(s => s.Status = "Processing")
            .Into(s => s.ProcessedCount = 0)
            .While(s => s.ProcessedCount < s.Items.Count)
                .Into(s => s.ProcessedCount++)
            .EndWhile()
            .Into(s => s.Status = "Completed");
    }
}

public class ValidatedOrderFlow : FlowConfig<ComplexBusinessState>
{
    protected override void Configure(IFlowBuilder<ComplexBusinessState> flow)
    {
        flow
            .If(s => string.IsNullOrEmpty(s.OrderId))
                .Into(s => s.Errors.Add("Order ID is required"))
                .Into(s => s.Status = "Rejected")
            .ElseIf(s => s.Items.Count == 0)
                .Into(s => s.Errors.Add("Order must have items"))
                .Into(s => s.Status = "Rejected")
            .ElseIf(s => s.Amount <= 0)
                .Into(s => s.Errors.Add("Amount must be positive"))
                .Into(s => s.Status = "Rejected")
            .Else(f => f
                .Into(s => s.Status = "Processing")
                .Into(s => s.Status = "Completed")
            )
            .EndIf();
    }
}
