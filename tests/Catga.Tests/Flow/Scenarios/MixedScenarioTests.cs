using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Mixed scenario tests combining multiple features and patterns.
/// Tests real-world combinations of conditions, loops, compensation, and parallel processing.
/// </summary>
public class MixedScenarioTests
{
    #region Test State

    public class ECommerceOrderState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public List<CartItem> Items { get; set; } = new();

        // Customer info
        public CustomerTier Tier { get; set; } = CustomerTier.Bronze;
        public bool HasPrimeShipping { get; set; }
        public int LoyaltyPoints { get; set; }

        // Order processing
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal ShippingCost { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }

        // Status tracking
        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public List<string> ProcessingLog { get; set; } = new();
        public List<string> Notifications { get; set; } = new();

        // Inventory
        public Dictionary<string, bool> ReservedItems { get; set; } = new();

        // Payment
        public string PaymentMethod { get; set; } = "Card";
        public bool PaymentAuthorized { get; set; }
        public string? TransactionId { get; set; }
    }

    public record CartItem(string Sku, string Name, int Quantity, decimal Price, string Category);

    public enum CustomerTier { Bronze, Silver, Gold, Platinum }
    public enum OrderStatus { Pending, Validating, Processing, PaymentPending, Paid, Shipping, Completed, Cancelled, Failed }

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
    public async Task MixedScenario_FullOrderProcessing_CompletesSuccessfully()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = CreateFullOrderProcessingFlow();
        var state = new ECommerceOrderState
        {
            FlowId = $"order-{Guid.NewGuid():N}",
            OrderId = "ORD-001",
            CustomerId = "CUST-001",
            Tier = CustomerTier.Gold,
            HasPrimeShipping = true,
            LoyaltyPoints = 5000,
            PaymentMethod = "Card",
            Items = new List<CartItem>
            {
                new("SKU-001", "Laptop", 1, 999.99m, "Electronics"),
                new("SKU-002", "Mouse", 2, 29.99m, "Electronics"),
                new("SKU-003", "Book", 3, 19.99m, "Books")
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Status.Should().Be(OrderStatus.Completed);
        result.State.Subtotal.Should().BeGreaterThan(0);
        result.State.Discount.Should().BeGreaterThan(0, "Gold tier should get discount");
        result.State.ShippingCost.Should().Be(0, "Prime shipping is free");
        result.State.PaymentAuthorized.Should().BeTrue();
        result.State.TransactionId.Should().NotBeNullOrEmpty();
        result.State.Notifications.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MixedScenario_OrderWithMixedInventory_PartialReservation()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ECommerceOrderState>("mixed-inventory")
            .Step("validate", async (state, ct) =>
            {
                state.Status = OrderStatus.Validating;
                state.ProcessingLog.Add("Validation started");
                return true;
            })
            .ForEach(
                s => s.Items,
                (item, f) => f
                    .Step($"check-{item.Sku}", async (state, ct) =>
                    {
                        state.ProcessingLog.Add($"Checking {item.Sku}");
                        return true;
                    })
                    .If(s => item.Quantity <= 10) // Simulate inventory availability
                        .Then(inner => inner.Step($"reserve-{item.Sku}", async (state, ct) =>
                        {
                            state.ReservedItems[item.Sku] = true;
                            state.ProcessingLog.Add($"Reserved {item.Sku}");
                            return true;
                        }))
                    .Else(inner => inner.Step($"backorder-{item.Sku}", async (state, ct) =>
                    {
                        state.ReservedItems[item.Sku] = false;
                        state.ProcessingLog.Add($"Backordered {item.Sku}");
                        state.Notifications.Add($"{item.Name} is on backorder");
                        return true;
                    }))
                    .EndIf())
            .Step("finalize", async (state, ct) =>
            {
                var allReserved = state.ReservedItems.Values.All(v => v);
                state.Status = allReserved ? OrderStatus.Processing : OrderStatus.Pending;
                return true;
            })
            .Build();

        var state = new ECommerceOrderState
        {
            FlowId = "inventory-test",
            Items = new List<CartItem>
            {
                new("SKU-001", "Item1", 5, 10m, "Cat1"),   // Will be reserved
                new("SKU-002", "Item2", 15, 20m, "Cat2"),  // Will be backordered
                new("SKU-003", "Item3", 3, 30m, "Cat3")    // Will be reserved
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ReservedItems["SKU-001"].Should().BeTrue();
        result.State.ReservedItems["SKU-002"].Should().BeFalse();
        result.State.ReservedItems["SKU-003"].Should().BeTrue();
        result.State.Notifications.Should().Contain(n => n.Contains("backorder"));
    }

    [Fact]
    public async Task MixedScenario_TieredDiscountWithLoyaltyPoints_CalculatesCorrectly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ECommerceOrderState>("tiered-discount")
            .Step("calculate-subtotal", async (state, ct) =>
            {
                state.Subtotal = state.Items.Sum(i => i.Price * i.Quantity);
                state.ProcessingLog.Add($"Subtotal: {state.Subtotal}");
                return true;
            })
            // Tier-based discount
            .Switch(s => s.Tier)
                .Case(CustomerTier.Platinum, f => f.Step("platinum-discount", async (state, ct) =>
                {
                    state.Discount = state.Subtotal * 0.20m;
                    state.ProcessingLog.Add("Applied Platinum 20% discount");
                    return true;
                }))
                .Case(CustomerTier.Gold, f => f.Step("gold-discount", async (state, ct) =>
                {
                    state.Discount = state.Subtotal * 0.15m;
                    state.ProcessingLog.Add("Applied Gold 15% discount");
                    return true;
                }))
                .Case(CustomerTier.Silver, f => f.Step("silver-discount", async (state, ct) =>
                {
                    state.Discount = state.Subtotal * 0.10m;
                    state.ProcessingLog.Add("Applied Silver 10% discount");
                    return true;
                }))
                .Default(f => f.Step("no-tier-discount", async (state, ct) =>
                {
                    state.Discount = 0;
                    state.ProcessingLog.Add("No tier discount");
                    return true;
                }))
            .EndSwitch()
            // Loyalty points bonus
            .If(s => s.LoyaltyPoints >= 1000)
                .Then(f => f.Step("loyalty-bonus", async (state, ct) =>
                {
                    var pointsDiscount = Math.Min(state.LoyaltyPoints / 100m, state.Subtotal * 0.1m);
                    state.Discount += pointsDiscount;
                    state.ProcessingLog.Add($"Applied loyalty points: -{pointsDiscount:F2}");
                    return true;
                }))
            .EndIf()
            // Calculate final
            .Step("calculate-final", async (state, ct) =>
            {
                state.Tax = (state.Subtotal - state.Discount) * 0.08m;
                state.Total = state.Subtotal - state.Discount + state.Tax + state.ShippingCost;
                state.ProcessingLog.Add($"Final total: {state.Total:F2}");
                return true;
            })
            .Build();

        var state = new ECommerceOrderState
        {
            FlowId = "discount-test",
            Tier = CustomerTier.Gold,
            LoyaltyPoints = 5000,
            Items = new List<CartItem>
            {
                new("SKU-001", "Product", 1, 1000m, "General")
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Subtotal.Should().Be(1000m);
        // Gold discount (15%) + Loyalty points (max 10% = 100)
        result.State.Discount.Should().Be(250m); // 150 + 100
        result.State.ProcessingLog.Should().Contain(l => l.Contains("Gold"));
        result.State.ProcessingLog.Should().Contain(l => l.Contains("loyalty"));
    }

    [Fact]
    public async Task MixedScenario_PaymentWithFallback_TriesMultipleMethods()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var attemptedMethods = new List<string>();

        var flow = FlowBuilder.Create<PaymentFallbackState>("payment-fallback")
            .Step("init", async (state, ct) =>
            {
                state.ProcessingLog.Add("Starting payment");
                return true;
            })
            // Try each payment method in order until one succeeds
            .ForEach(
                s => s.PaymentMethods,
                (method, f) => f
                    .If(s => !s.PaymentSucceeded)
                        .Then(inner => inner.Step($"try-{method}", async (state, ct) =>
                        {
                            attemptedMethods.Add(method);
                            state.ProcessingLog.Add($"Trying {method}");

                            // Simulate: First two methods fail, third succeeds
                            if (attemptedMethods.Count >= 3)
                            {
                                state.PaymentSucceeded = true;
                                state.SuccessfulMethod = method;
                                state.ProcessingLog.Add($"{method} succeeded");
                            }
                            else
                            {
                                state.ProcessingLog.Add($"{method} failed");
                            }
                            return true;
                        }))
                    .EndIf())
            .Step("finalize", async (state, ct) =>
            {
                if (state.PaymentSucceeded)
                {
                    state.ProcessingLog.Add($"Payment completed via {state.SuccessfulMethod}");
                }
                else
                {
                    state.ProcessingLog.Add("All payment methods failed");
                }
                return true;
            })
            .Build();

        var state = new PaymentFallbackState
        {
            FlowId = "payment-test",
            PaymentMethods = new List<string> { "Card", "PayPal", "BankTransfer", "Crypto" }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.PaymentSucceeded.Should().BeTrue();
        result.State.SuccessfulMethod.Should().Be("BankTransfer");
        attemptedMethods.Should().HaveCount(3);
    }

    [Fact]
    public async Task MixedScenario_ShippingWithConditions_SelectsOptimalMethod()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ShippingState>("shipping-selection")
            .Step("calculate-weight", async (state, ct) =>
            {
                state.TotalWeight = state.Items.Sum(i => i.Weight * i.Quantity);
                state.ProcessingLog.Add($"Total weight: {state.TotalWeight}kg");
                return true;
            })
            // Determine shipping method based on multiple conditions
            .If(s => s.IsExpress && s.TotalWeight < 5)
                .Then(f => f.Step("express-air", async (state, ct) =>
                {
                    state.ShippingMethod = "Express Air";
                    state.ShippingCost = 25m + (state.TotalWeight * 5m);
                    state.EstimatedDays = 1;
                    return true;
                }))
            .ElseIf(s => s.IsExpress)
                .Then(f => f.Step("express-ground", async (state, ct) =>
                {
                    state.ShippingMethod = "Express Ground";
                    state.ShippingCost = 15m + (state.TotalWeight * 3m);
                    state.EstimatedDays = 2;
                    return true;
                }))
            .ElseIf(s => s.TotalWeight > 20)
                .Then(f => f.Step("freight", async (state, ct) =>
                {
                    state.ShippingMethod = "Freight";
                    state.ShippingCost = 50m + (state.TotalWeight * 1m);
                    state.EstimatedDays = 7;
                    return true;
                }))
            .ElseIf(s => s.HasPrimeShipping)
                .Then(f => f.Step("prime-shipping", async (state, ct) =>
                {
                    state.ShippingMethod = "Prime";
                    state.ShippingCost = 0m;
                    state.EstimatedDays = 2;
                    return true;
                }))
            .Else(f => f.Step("standard-shipping", async (state, ct) =>
            {
                state.ShippingMethod = "Standard";
                state.ShippingCost = 5m + (state.TotalWeight * 0.5m);
                state.EstimatedDays = 5;
                return true;
            }))
            .EndIf()
            .Step("log-selection", async (state, ct) =>
            {
                state.ProcessingLog.Add($"Selected: {state.ShippingMethod}, Cost: {state.ShippingCost:F2}, Days: {state.EstimatedDays}");
                return true;
            })
            .Build();

        // Test Express with light package
        var expressLight = new ShippingState
        {
            FlowId = "express-light",
            IsExpress = true,
            Items = new List<ShippingItem> { new("Item1", 1, 2m) }
        };
        var result1 = await executor.ExecuteAsync(flow, expressLight);
        result1.State.ShippingMethod.Should().Be("Express Air");

        // Test Heavy package
        var heavy = new ShippingState
        {
            FlowId = "heavy",
            Items = new List<ShippingItem> { new("Heavy", 1, 25m) }
        };
        var result2 = await executor.ExecuteAsync(flow, heavy);
        result2.State.ShippingMethod.Should().Be("Freight");

        // Test Prime shipping
        var prime = new ShippingState
        {
            FlowId = "prime",
            HasPrimeShipping = true,
            Items = new List<ShippingItem> { new("Item", 1, 3m) }
        };
        var result3 = await executor.ExecuteAsync(flow, prime);
        result3.State.ShippingMethod.Should().Be("Prime");
        result3.State.ShippingCost.Should().Be(0);
    }

    private IFlow<ECommerceOrderState> CreateFullOrderProcessingFlow()
    {
        return FlowBuilder.Create<ECommerceOrderState>("full-order-processing")
            // 1. Validation
            .Step("validate-order", async (state, ct) =>
            {
                state.Status = OrderStatus.Validating;
                state.ProcessingLog.Add("Order validation started");
                if (state.Items.Count == 0) return false;
                state.ProcessingLog.Add("Order validated");
                return true;
            })
            // 2. Calculate subtotal
            .Step("calculate-subtotal", async (state, ct) =>
            {
                state.Subtotal = state.Items.Sum(i => i.Price * i.Quantity);
                state.ProcessingLog.Add($"Subtotal calculated: {state.Subtotal}");
                return true;
            })
            // 3. Apply tier discount
            .Switch(s => s.Tier)
                .Case(CustomerTier.Platinum, f => f.Step("platinum", async (state, ct) => { state.Discount = state.Subtotal * 0.2m; return true; }))
                .Case(CustomerTier.Gold, f => f.Step("gold", async (state, ct) => { state.Discount = state.Subtotal * 0.15m; return true; }))
                .Case(CustomerTier.Silver, f => f.Step("silver", async (state, ct) => { state.Discount = state.Subtotal * 0.1m; return true; }))
                .Default(f => f.Step("bronze", async (state, ct) => { state.Discount = 0; return true; }))
            .EndSwitch()
            // 4. Calculate shipping
            .If(s => s.HasPrimeShipping)
                .Then(f => f.Step("free-shipping", async (state, ct) => { state.ShippingCost = 0; return true; }))
                .Else(f => f.Step("standard-shipping", async (state, ct) => { state.ShippingCost = 9.99m; return true; }))
            .EndIf()
            // 5. Calculate tax and total
            .Step("calculate-total", async (state, ct) =>
            {
                state.Tax = (state.Subtotal - state.Discount) * 0.08m;
                state.Total = state.Subtotal - state.Discount + state.Tax + state.ShippingCost;
                state.ProcessingLog.Add($"Total: {state.Total}");
                return true;
            })
            // 6. Reserve inventory
            .ForEach(
                s => s.Items,
                (item, f) => f.Step($"reserve-{item.Sku}", async (state, ct) =>
                {
                    state.ReservedItems[item.Sku] = true;
                    return true;
                }))
            // 7. Process payment
            .Step("process-payment", async (state, ct) =>
            {
                state.Status = OrderStatus.PaymentPending;
                state.PaymentAuthorized = true;
                state.TransactionId = $"TXN-{Guid.NewGuid():N}"[..16];
                state.Status = OrderStatus.Paid;
                state.ProcessingLog.Add($"Payment processed: {state.TransactionId}");
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.PaymentAuthorized = false;
                state.TransactionId = null;
                state.ProcessingLog.Add("Payment refunded");
            })
            // 8. Send notification
            .Step("send-notification", async (state, ct) =>
            {
                state.Notifications.Add($"Order {state.OrderId} confirmed");
                state.Status = OrderStatus.Completed;
                state.ProcessingLog.Add("Notification sent");
                return true;
            })
            .Build();
    }

    public class PaymentFallbackState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<string> PaymentMethods { get; set; } = new();
        public bool PaymentSucceeded { get; set; }
        public string? SuccessfulMethod { get; set; }
        public List<string> ProcessingLog { get; set; } = new();
    }

    public class ShippingState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<ShippingItem> Items { get; set; } = new();
        public decimal TotalWeight { get; set; }
        public bool IsExpress { get; set; }
        public bool HasPrimeShipping { get; set; }
        public string ShippingMethod { get; set; } = "";
        public decimal ShippingCost { get; set; }
        public int EstimatedDays { get; set; }
        public List<string> ProcessingLog { get; set; } = new();
    }

    public record ShippingItem(string Name, int Quantity, decimal Weight);

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
