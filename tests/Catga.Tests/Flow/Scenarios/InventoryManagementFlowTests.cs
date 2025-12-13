using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Inventory management flow scenarios including stock reservation, allocation, and replenishment.
/// </summary>
public class InventoryManagementFlowTests
{
    #region Test State

    public class InventoryState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string WarehouseId { get; set; } = "";
        public List<StockItem> Items { get; set; } = new();
        public Dictionary<string, int> ReservedStock { get; set; } = new();
        public Dictionary<string, int> AllocatedStock { get; set; } = new();

        // Processing flags
        public bool StockVerified { get; set; }
        public bool StockReserved { get; set; }
        public bool StockAllocated { get; set; }
        public bool ShipmentCreated { get; set; }

        // Results
        public string? ShipmentId { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public record StockItem(string Sku, int RequestedQuantity, int AvailableQuantity);

    #endregion

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResiliencePipelineProvider, DefaultResiliencePipelineProvider>();
        services.AddSingleton<IMessageSerializer, TestSerializer>();
        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();
        services.AddSingleton<IDslFlowExecutor, DslFlowExecutor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task InventoryFlow_FullStockAvailable_CompletesSuccessfully()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<InventoryState>("inventory-allocation")
            .Step("verify-stock", async (state, ct) =>
            {
                foreach (var item in state.Items)
                {
                    if (item.AvailableQuantity < item.RequestedQuantity)
                    {
                        state.Errors.Add($"Insufficient stock for {item.Sku}");
                        return false;
                    }
                }
                state.StockVerified = true;
                return true;
            })
            .Step("reserve-stock", async (state, ct) =>
            {
                foreach (var item in state.Items)
                {
                    state.ReservedStock[item.Sku] = item.RequestedQuantity;
                }
                state.StockReserved = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.ReservedStock.Clear();
                state.StockReserved = false;
            })
            .Step("allocate-stock", async (state, ct) =>
            {
                foreach (var (sku, qty) in state.ReservedStock)
                {
                    state.AllocatedStock[sku] = qty;
                }
                state.StockAllocated = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.AllocatedStock.Clear();
                state.StockAllocated = false;
            })
            .Step("create-shipment", async (state, ct) =>
            {
                state.ShipmentId = $"SHIP-{Guid.NewGuid():N}"[..12];
                state.ShipmentCreated = true;
                return true;
            })
            .Build();

        var initialState = new InventoryState
        {
            FlowId = $"inv-{Guid.NewGuid():N}",
            WarehouseId = "WH-001",
            Items = new List<StockItem>
            {
                new("SKU-001", 5, 100),
                new("SKU-002", 10, 50),
                new("SKU-003", 2, 10)
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.StockVerified.Should().BeTrue();
        result.State.StockReserved.Should().BeTrue();
        result.State.StockAllocated.Should().BeTrue();
        result.State.ShipmentCreated.Should().BeTrue();
        result.State.ShipmentId.Should().NotBeNullOrEmpty();
        result.State.AllocatedStock.Should().HaveCount(3);
    }

    [Fact]
    public async Task InventoryFlow_InsufficientStock_FailsAtVerification()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<InventoryState>("insufficient-stock")
            .Step("verify-stock", async (state, ct) =>
            {
                foreach (var item in state.Items)
                {
                    if (item.AvailableQuantity < item.RequestedQuantity)
                    {
                        state.Errors.Add($"Insufficient stock for {item.Sku}: requested {item.RequestedQuantity}, available {item.AvailableQuantity}");
                        return false;
                    }
                }
                state.StockVerified = true;
                return true;
            })
            .Step("reserve-stock", async (state, ct) =>
            {
                state.StockReserved = true;
                return true;
            })
            .Build();

        var initialState = new InventoryState
        {
            FlowId = "insufficient-stock-flow",
            Items = new List<StockItem>
            {
                new("SKU-001", 100, 10) // Requesting more than available
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.StockVerified.Should().BeFalse();
        result.State.StockReserved.Should().BeFalse();
        result.State.Errors.Should().ContainSingle()
            .Which.Should().Contain("Insufficient stock");
    }

    [Fact]
    public async Task InventoryFlow_MultiWarehouse_ProcessesInParallel()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var warehousesProcessed = new List<string>();

        var flow = FlowBuilder.Create<MultiWarehouseState>("multi-warehouse")
            .ForEach(
                state => state.Warehouses,
                (warehouse, f) => f.Step($"check-{warehouse}", async (state, ct) =>
                {
                    lock (warehousesProcessed)
                    {
                        warehousesProcessed.Add(warehouse);
                    }
                    state.ProcessedWarehouses.Add(warehouse);
                    return true;
                }))
            .WithParallelism(3)
            .Step("consolidate-results", async (state, ct) =>
            {
                state.ConsolidationComplete = true;
                return true;
            })
            .Build();

        var initialState = new MultiWarehouseState
        {
            FlowId = "multi-wh-flow",
            Warehouses = new List<string> { "WH-001", "WH-002", "WH-003", "WH-004", "WH-005" }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedWarehouses.Should().HaveCount(5);
        result.State.ConsolidationComplete.Should().BeTrue();
        warehousesProcessed.Should().HaveCount(5);
    }

    [Fact]
    public async Task InventoryFlow_ReplenishmentDecision_UsesConditionalLogic()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ReplenishmentState>("replenishment-decision")
            .Step("check-stock-level", async (state, ct) =>
            {
                state.StockLevel = state.CurrentStock * 100 / state.MaxStock;
                return true;
            })
            .If(state => state.StockLevel <= 20)
                .Then(f => f.Step("urgent-replenish", async (state, ct) =>
                {
                    state.ReplenishmentType = "URGENT";
                    state.OrderQuantity = state.MaxStock - state.CurrentStock;
                    return true;
                }))
            .ElseIf(state => state.StockLevel <= 50)
                .Then(f => f.Step("normal-replenish", async (state, ct) =>
                {
                    state.ReplenishmentType = "NORMAL";
                    state.OrderQuantity = (state.MaxStock - state.CurrentStock) / 2;
                    return true;
                }))
            .Else(f => f.Step("no-replenish", async (state, ct) =>
            {
                state.ReplenishmentType = "NONE";
                state.OrderQuantity = 0;
                return true;
            }))
            .EndIf()
            .Build();

        // Test urgent replenishment (10% stock)
        var urgentState = new ReplenishmentState { FlowId = "urgent", CurrentStock = 10, MaxStock = 100 };
        var urgentResult = await executor.ExecuteAsync(flow, urgentState);

        urgentResult.IsSuccess.Should().BeTrue();
        urgentResult.State.ReplenishmentType.Should().Be("URGENT");
        urgentResult.State.OrderQuantity.Should().Be(90);

        // Test normal replenishment (40% stock)
        var normalState = new ReplenishmentState { FlowId = "normal", CurrentStock = 40, MaxStock = 100 };
        var normalResult = await executor.ExecuteAsync(flow, normalState);

        normalResult.IsSuccess.Should().BeTrue();
        normalResult.State.ReplenishmentType.Should().Be("NORMAL");
        normalResult.State.OrderQuantity.Should().Be(30);

        // Test no replenishment (80% stock)
        var highState = new ReplenishmentState { FlowId = "high", CurrentStock = 80, MaxStock = 100 };
        var highResult = await executor.ExecuteAsync(flow, highState);

        highResult.IsSuccess.Should().BeTrue();
        highResult.State.ReplenishmentType.Should().Be("NONE");
        highResult.State.OrderQuantity.Should().Be(0);
    }

    public class MultiWarehouseState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> Warehouses { get; set; } = new();
        public List<string> ProcessedWarehouses { get; set; } = new();
        public bool ConsolidationComplete { get; set; }
    }

    public class ReplenishmentState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int CurrentStock { get; set; }
        public int MaxStock { get; set; }
        public int StockLevel { get; set; } // Percentage
        public string? ReplenishmentType { get; set; }
        public int OrderQuantity { get; set; }
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
