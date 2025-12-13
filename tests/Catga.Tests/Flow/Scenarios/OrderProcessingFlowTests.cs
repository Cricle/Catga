using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Real-world order processing flow scenarios.
/// Tests complete business workflows with compensation and error handling.
/// </summary>
public class OrderProcessingFlowTests
{
    #region Test State

    public class OrderProcessingState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public List<OrderItem> Items { get; set; } = new();
        public decimal TotalAmount { get; set; }

        // Processing state
        public bool InventoryReserved { get; set; }
        public bool PaymentProcessed { get; set; }
        public bool OrderConfirmed { get; set; }
        public bool NotificationSent { get; set; }

        // Error tracking
        public List<string> Errors { get; set; } = new();
        public bool CompensationExecuted { get; set; }
    }

    public record OrderItem(string ProductId, string Name, int Quantity, decimal Price);

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
    public async Task OrderFlow_HappyPath_CompletesSuccessfully()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<OrderProcessingState>("order-processing")
            .Step("validate-order", async (state, ct) =>
            {
                state.TotalAmount = state.Items.Sum(i => i.Price * i.Quantity);
                return state.Items.Count > 0 && state.TotalAmount > 0;
            })
            .Step("reserve-inventory", async (state, ct) =>
            {
                // Simulate inventory reservation
                await Task.Delay(10, ct);
                state.InventoryReserved = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.InventoryReserved = false;
                state.CompensationExecuted = true;
            })
            .Step("process-payment", async (state, ct) =>
            {
                // Simulate payment processing
                await Task.Delay(10, ct);
                state.PaymentProcessed = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.PaymentProcessed = false;
                state.CompensationExecuted = true;
            })
            .Step("confirm-order", async (state, ct) =>
            {
                state.OrderConfirmed = true;
                return true;
            })
            .Step("send-notification", async (state, ct) =>
            {
                state.NotificationSent = true;
                return true;
            })
            .Build();

        var initialState = new OrderProcessingState
        {
            FlowId = $"order-{Guid.NewGuid():N}",
            OrderId = "ORD-001",
            CustomerId = "CUST-001",
            Items = new List<OrderItem>
            {
                new("P1", "Laptop", 1, 999.99m),
                new("P2", "Mouse", 2, 29.99m)
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.InventoryReserved.Should().BeTrue();
        result.State.PaymentProcessed.Should().BeTrue();
        result.State.OrderConfirmed.Should().BeTrue();
        result.State.NotificationSent.Should().BeTrue();
        result.State.TotalAmount.Should().Be(1059.97m);
    }

    [Fact]
    public async Task OrderFlow_PaymentFails_CompensatesInventory()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var paymentAttempts = 0;

        var flow = FlowBuilder.Create<OrderProcessingState>("order-with-payment-failure")
            .Step("reserve-inventory", async (state, ct) =>
            {
                state.InventoryReserved = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.InventoryReserved = false;
                state.CompensationExecuted = true;
            })
            .Step("process-payment", async (state, ct) =>
            {
                paymentAttempts++;
                // Simulate payment failure
                throw new InvalidOperationException("Payment declined");
            })
            .Build();

        var initialState = new OrderProcessingState
        {
            FlowId = $"order-{Guid.NewGuid():N}",
            Items = new List<OrderItem> { new("P1", "Product", 1, 100m) }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.CompensationExecuted.Should().BeTrue();
        result.State.InventoryReserved.Should().BeFalse("inventory should be released after compensation");
    }

    [Fact]
    public async Task OrderFlow_WithConditionalShipping_SelectsCorrectBranch()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var shippingMethod = "";

        var flow = FlowBuilder.Create<OrderProcessingState>("conditional-shipping")
            .Step("calculate-total", async (state, ct) =>
            {
                state.TotalAmount = state.Items.Sum(i => i.Price * i.Quantity);
                return true;
            })
            .If(state => state.TotalAmount >= 100)
                .Then(f => f.Step("free-shipping", async (state, ct) =>
                {
                    shippingMethod = "FREE";
                    return true;
                }))
                .Else(f => f.Step("standard-shipping", async (state, ct) =>
                {
                    shippingMethod = "STANDARD";
                    state.TotalAmount += 9.99m;
                    return true;
                }))
            .EndIf()
            .Build();

        // Act - High value order
        var highValueState = new OrderProcessingState
        {
            FlowId = "high-value",
            Items = new List<OrderItem> { new("P1", "Expensive", 1, 500m) }
        };
        var highResult = await executor.ExecuteAsync(flow, highValueState);

        // Assert
        highResult.IsSuccess.Should().BeTrue();
        shippingMethod.Should().Be("FREE");

        // Act - Low value order
        shippingMethod = "";
        var lowValueState = new OrderProcessingState
        {
            FlowId = "low-value",
            Items = new List<OrderItem> { new("P2", "Cheap", 1, 10m) }
        };
        var lowResult = await executor.ExecuteAsync(flow, lowValueState);

        // Assert
        lowResult.IsSuccess.Should().BeTrue();
        shippingMethod.Should().Be("STANDARD");
        lowResult.State.TotalAmount.Should().Be(19.99m);
    }

    [Fact]
    public async Task OrderFlow_BulkItems_ProcessesWithForEach()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var processedItems = new List<string>();

        var flow = FlowBuilder.Create<OrderProcessingState>("bulk-processing")
            .ForEach(
                state => state.Items,
                (item, f) => f.Step($"process-{item.ProductId}", async (state, ct) =>
                {
                    processedItems.Add(item.ProductId);
                    return true;
                }))
            .Step("finalize", async (state, ct) =>
            {
                state.OrderConfirmed = true;
                return true;
            })
            .Build();

        var initialState = new OrderProcessingState
        {
            FlowId = "bulk-order",
            Items = new List<OrderItem>
            {
                new("P1", "Item1", 1, 10m),
                new("P2", "Item2", 2, 20m),
                new("P3", "Item3", 3, 30m)
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, initialState);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.OrderConfirmed.Should().BeTrue();
        processedItems.Should().HaveCount(3);
        processedItems.Should().Contain(new[] { "P1", "P2", "P3" });
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
