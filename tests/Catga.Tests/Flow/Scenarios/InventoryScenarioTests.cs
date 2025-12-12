using Catga.Flow.Dsl;
using FluentAssertions;
using Xunit;
using NSubstitute;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Scenario tests for inventory management workflows
/// Tests stock reservation, allocation, and fulfillment scenarios
/// </summary>
public class InventoryScenarioTests
{
    [Fact]
    public async Task Inventory_ReserveStock_ShouldAllocateSuccessfully()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ReserveStockFlow();
        var executor = new DslFlowExecutor<InventoryScenarioState, ReserveStockFlow>(mediator, store, config);

        var state = new InventoryScenarioState
        {
            FlowId = "inventory-reserve",
            OrderId = "ORD-001",
            Items = new() { ("SKU-001", 5), ("SKU-002", 3) },
            AvailableStock = new() { ("SKU-001", 100), ("SKU-002", 50) }
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Status.Should().Be("Reserved");
        state.ReservedItems.Should().HaveCount(2);
    }

    [Fact]
    public async Task Inventory_InsufficientStock_ShouldFail()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ReserveStockWithValidationFlow();
        var executor = new DslFlowExecutor<InventoryScenarioState, ReserveStockWithValidationFlow>(mediator, store, config);

        var state = new InventoryScenarioState
        {
            FlowId = "inventory-insufficient",
            OrderId = "ORD-002",
            Items = new() { ("SKU-001", 200) },  // Request more than available
            AvailableStock = new() { ("SKU-001", 50) }
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        state.Status.Should().Be("Failed");
        state.ErrorMessages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Inventory_MultipleItems_ShouldProcessSequentially()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new MultiItemReservationFlow();
        var executor = new DslFlowExecutor<InventoryScenarioState, MultiItemReservationFlow>(mediator, store, config);

        var state = new InventoryScenarioState
        {
            FlowId = "inventory-multi",
            OrderId = "ORD-003",
            Items = new() { ("SKU-001", 5), ("SKU-002", 3), ("SKU-003", 2) },
            AvailableStock = new() { ("SKU-001", 100), ("SKU-002", 50), ("SKU-003", 30) }
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.ReservedItems.Should().HaveCount(3);
    }

    [Fact]
    public async Task Inventory_ReleaseReservation_ShouldFreeStock()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new ReleaseReservationFlow();
        var executor = new DslFlowExecutor<InventoryScenarioState, ReleaseReservationFlow>(mediator, store, config);

        var state = new InventoryScenarioState
        {
            FlowId = "inventory-release",
            OrderId = "ORD-004",
            ReservedItems = new() { ("SKU-001", 5), ("SKU-002", 3) },
            Status = "Reserved"
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Status.Should().Be("Released");
        state.ReservedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task Inventory_LowStockWarning_ShouldAlert()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var store = new InMemoryDslFlowStore();
        var config = new LowStockAlertFlow();
        var executor = new DslFlowExecutor<InventoryScenarioState, LowStockAlertFlow>(mediator, store, config);

        var state = new InventoryScenarioState
        {
            FlowId = "inventory-low-stock",
            OrderId = "ORD-005",
            Items = new() { ("SKU-001", 5) },
            AvailableStock = new() { ("SKU-001", 10) }
        };
        SetupMediator(mediator);

        // Act
        var result = await executor.RunAsync(state);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        state.Alerts.Should().NotBeEmpty();
    }

    private void SetupMediator(ICatgaMediator mediator)
    {
        mediator.SendAsync(Arg.Any<IRequest>())
            .Returns(Task.CompletedTask);
    }
}

// Test state for inventory scenarios
public class InventoryScenarioState : TestStateBase
{
    public string OrderId { get; set; }
    public List<(string Sku, int Quantity)> Items { get; set; } = new();
    public Dictionary<string, int> AvailableStock { get; set; } = new();
    public List<(string Sku, int Quantity)> ReservedItems { get; set; } = new();
    public string Status { get; set; } = "Pending";
    public List<string> ErrorMessages { get; set; } = new();
    public List<string> Alerts { get; set; } = new();
}

// Inventory flow configurations
public class ReserveStockFlow : FlowConfig<InventoryScenarioState>
{
    protected override void Configure(IFlowBuilder<InventoryScenarioState> flow)
    {
        flow
            .Into(s => s.Status = "Processing")
            .While(s => s.ReservedItems.Count < s.Items.Count)
                .Into(s =>
                {
                    if (s.ReservedItems.Count < s.Items.Count)
                    {
                        var item = s.Items[s.ReservedItems.Count];
                        s.ReservedItems.Add(item);
                    }
                })
            .EndWhile()
            .Into(s => s.Status = "Reserved");
    }
}

public class ReserveStockWithValidationFlow : FlowConfig<InventoryScenarioState>
{
    protected override void Configure(IFlowBuilder<InventoryScenarioState> flow)
    {
        flow
            .Into(s => s.Status = "Validating")
            .While(s => s.ReservedItems.Count < s.Items.Count)
                .Into(s =>
                {
                    if (s.ReservedItems.Count < s.Items.Count)
                    {
                        var item = s.Items[s.ReservedItems.Count];
                        if (s.AvailableStock.TryGetValue(item.Sku, out var available) && available >= item.Quantity)
                        {
                            s.ReservedItems.Add(item);
                        }
                        else
                        {
                            s.ErrorMessages.Add($"Insufficient stock for {item.Sku}");
                            s.Status = "Failed";
                        }
                    }
                })
                .BreakIf(s => s.Status == "Failed")
            .EndWhile()
            .If(s => s.Status != "Failed")
                .Into(s => s.Status = "Reserved")
            .EndIf();
    }
}

public class MultiItemReservationFlow : FlowConfig<InventoryScenarioState>
{
    protected override void Configure(IFlowBuilder<InventoryScenarioState> flow)
    {
        flow
            .Into(s => s.Status = "Processing")
            .While(s => s.ReservedItems.Count < s.Items.Count)
                .Into(s =>
                {
                    if (s.ReservedItems.Count < s.Items.Count)
                    {
                        var item = s.Items[s.ReservedItems.Count];
                        s.ReservedItems.Add(item);
                    }
                })
            .EndWhile()
            .Into(s => s.Status = "Reserved");
    }
}

public class ReleaseReservationFlow : FlowConfig<InventoryScenarioState>
{
    protected override void Configure(IFlowBuilder<InventoryScenarioState> flow)
    {
        flow
            .Into(s => s.Status = "Releasing")
            .Into(s => s.ReservedItems.Clear())
            .Into(s => s.Status = "Released");
    }
}

public class LowStockAlertFlow : FlowConfig<InventoryScenarioState>
{
    protected override void Configure(IFlowBuilder<InventoryScenarioState> flow)
    {
        flow
            .Into(s => s.Status = "Processing")
            .While(s => s.ReservedItems.Count < s.Items.Count)
                .Into(s =>
                {
                    if (s.ReservedItems.Count < s.Items.Count)
                    {
                        var item = s.Items[s.ReservedItems.Count];
                        s.ReservedItems.Add(item);

                        if (s.AvailableStock.TryGetValue(item.Sku, out var available) && available < 20)
                        {
                            s.Alerts.Add($"Low stock warning for {item.Sku}: {available} units remaining");
                        }
                    }
                })
            .EndWhile()
            .Into(s => s.Status = "Completed");
    }
}
