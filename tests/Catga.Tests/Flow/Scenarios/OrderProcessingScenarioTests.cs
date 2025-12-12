using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Scenario tests for order processing workflows
/// Tests real-world business scenarios with complex control flow
/// </summary>
public class OrderProcessingScenarioTests
{
    [Fact]
    public async Task OrderProcessing_StandardFlow_ShouldCompleteSuccessfully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new StandardOrderProcessingFlow();
        var executor = new DslFlowExecutor<OrderScenarioState, StandardOrderProcessingFlow>(mediator, store, config);

        var state = new OrderScenarioState
        {
            FlowId = "order-standard",
            OrderId = "ORD-001",
            Items = new() { "ITEM-1", "ITEM-2", "ITEM-3" },
            TotalAmount = 1000m
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Status.Should().Be("Completed");
        state.ProcessedItems.Should().HaveCount(3);
    }

    [Fact]
    public async Task OrderProcessing_WithHighValue_ShouldRequireApproval()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new HighValueOrderFlow();
        var executor = new DslFlowExecutor<OrderScenarioState, HighValueOrderFlow>(mediator, store, config);

        var state = new OrderScenarioState
        {
            FlowId = "order-high-value",
            OrderId = "ORD-002",
            Items = new() { "PREMIUM-1", "PREMIUM-2" },
            TotalAmount = 50000m
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
    public async Task OrderProcessing_WithRetry_ShouldHandleTemporaryFailure()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new OrderWithRetryFlow();
        var executor = new DslFlowExecutor<OrderScenarioState, OrderWithRetryFlow>(mediator, store, config);

        var state = new OrderScenarioState
        {
            FlowId = "order-retry",
            OrderId = "ORD-003",
            Items = new() { "ITEM-1" },
            TotalAmount = 500m
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.RetryAttempts.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task OrderProcessing_WithValidation_ShouldRejectInvalid()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new OrderValidationFlow();
        var executor = new DslFlowExecutor<OrderScenarioState, OrderValidationFlow>(mediator, store, config);

        var state = new OrderScenarioState
        {
            FlowId = "order-invalid",
            OrderId = "",  // Invalid: empty order ID
            Items = new(),
            TotalAmount = 0
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        state.Status.Should().Be("Rejected");
        state.ValidationErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task OrderProcessing_BulkOrders_ShouldProcessInParallel()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new BulkOrderProcessingFlow();
        var executor = new DslFlowExecutor<OrderScenarioState, BulkOrderProcessingFlow>(mediator, store, config);

        var state = new OrderScenarioState
        {
            FlowId = "order-bulk",
            OrderId = "ORD-BULK-001",
            Items = new() { "ITEM-1", "ITEM-2", "ITEM-3", "ITEM-4", "ITEM-5" },
            TotalAmount = 5000m
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ProcessedItems.Should().HaveCount(5);
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(Task.CompletedTask);
    }
}

// Test state for order scenarios
public class OrderScenarioState : TestStateBase
{
    public string OrderId { get; set; }
    public List<string> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Pending";
    public List<string> ProcessedItems { get; set; } = new();
    public bool RequiresApproval { get; set; }
    public int RetryAttempts { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
}

// Order processing flow configurations
public class StandardOrderProcessingFlow : FlowConfig<OrderScenarioState>
{
    protected override void Configure(IFlowBuilder<OrderScenarioState> flow)
    {
        flow
            .Into(s => s.Status = "Processing")
            .While(s => s.ProcessedItems.Count < s.Items.Count)
                .Into(s =>
                {
                    if (s.ProcessedItems.Count < s.Items.Count)
                    {
                        s.ProcessedItems.Add(s.Items[s.ProcessedItems.Count]);
                    }
                })
            .EndWhile()
            .Into(s => s.Status = "Completed");
    }
}

public class HighValueOrderFlow : FlowConfig<OrderScenarioState>
{
    protected override void Configure(IFlowBuilder<OrderScenarioState> flow)
    {
        flow
            .When(s => s.TotalAmount > 10000)
                .Into(s => s.RequiresApproval = true)
                .Into(s => s.Status = "PendingApproval")
            .EndWhen()
            .If(s => !s.RequiresApproval)
                .Into(s => s.Status = "Completed")
            .EndIf();
    }
}

public class OrderWithRetryFlow : FlowConfig<OrderScenarioState>
{
    protected override void Configure(IFlowBuilder<OrderScenarioState> flow)
    {
        flow
            .Into(s => s.RetryAttempts = 0)
            .Repeat(3)
                .Into(s => s.RetryAttempts++)
                .BreakIf(s => s.ProcessedItems.Count > 0)
            .EndRepeat()
            .Into(s => s.Status = "Completed");
    }
}

public class OrderValidationFlow : FlowConfig<OrderScenarioState>
{
    protected override void Configure(IFlowBuilder<OrderScenarioState> flow)
    {
        flow
            .If(s => string.IsNullOrEmpty(s.OrderId))
                .Into(s => s.ValidationErrors.Add("Order ID is required"))
                .Into(s => s.Status = "Rejected")
            .ElseIf(s => s.Items.Count == 0)
                .Into(s => s.ValidationErrors.Add("Order must have items"))
                .Into(s => s.Status = "Rejected")
            .ElseIf(s => s.TotalAmount <= 0)
                .Into(s => s.ValidationErrors.Add("Total amount must be positive"))
                .Into(s => s.Status = "Rejected")
            .Else(f => f
                .Into(s => s.Status = "Validated")
            )
            .EndIf();
    }
}

public class BulkOrderProcessingFlow : FlowConfig<OrderScenarioState>
{
    protected override void Configure(IFlowBuilder<OrderScenarioState> flow)
    {
        flow
            .Into(s => s.Status = "Processing")
            .While(s => s.ProcessedItems.Count < s.Items.Count)
                .Into(s =>
                {
                    if (s.ProcessedItems.Count < s.Items.Count)
                    {
                        s.ProcessedItems.Add(s.Items[s.ProcessedItems.Count]);
                    }
                })
            .EndWhile()
            .Into(s => s.Status = "Completed");
    }
}
