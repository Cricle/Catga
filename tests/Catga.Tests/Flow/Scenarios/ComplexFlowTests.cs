using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Complex flow scenarios with nested structures, multiple branches, and sophisticated logic.
/// </summary>
public class ComplexFlowTests
{
    #region Test State

    public class ComplexState : IFlowState
    {
        public string FlowId { get; set; } = "";

        // Order data
        public string OrderId { get; set; } = "";
        public decimal Amount { get; set; }
        public string CustomerType { get; set; } = "Regular"; // Regular, Premium, VIP
        public string PaymentMethod { get; set; } = "Card";
        public string ShippingMethod { get; set; } = "Standard";

        // Processing flags
        public bool Validated { get; set; }
        public bool FraudChecked { get; set; }
        public bool PaymentProcessed { get; set; }
        public bool InventoryReserved { get; set; }
        public bool ShippingScheduled { get; set; }
        public bool NotificationSent { get; set; }

        // Discounts and fees
        public decimal Discount { get; set; }
        public decimal ShippingFee { get; set; }
        public decimal FinalAmount { get; set; }

        // Tracking
        public List<string> ExecutedSteps { get; set; } = new();
        public List<string> AppliedRules { get; set; } = new();
    }

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
    public async Task ComplexFlow_NestedIfElse_ExecutesCorrectPath()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ComplexState>("nested-conditions")
            .Step("validate", async (state, ct) =>
            {
                state.Validated = true;
                state.ExecutedSteps.Add("validate");
                return true;
            })
            // Outer if: Amount > 100
            .If(s => s.Amount > 100)
                .Then(f => f
                    // Inner if: Customer type
                    .If(s => s.CustomerType == "VIP")
                        .Then(inner => inner.Step("vip-discount", async (state, ct) =>
                        {
                            state.Discount = state.Amount * 0.2m;
                            state.AppliedRules.Add("VIP-20%");
                            state.ExecutedSteps.Add("vip-discount");
                            return true;
                        }))
                    .ElseIf(s => s.CustomerType == "Premium")
                        .Then(inner => inner.Step("premium-discount", async (state, ct) =>
                        {
                            state.Discount = state.Amount * 0.1m;
                            state.AppliedRules.Add("Premium-10%");
                            state.ExecutedSteps.Add("premium-discount");
                            return true;
                        }))
                    .Else(inner => inner.Step("regular-discount", async (state, ct) =>
                    {
                        state.Discount = state.Amount * 0.05m;
                        state.AppliedRules.Add("Regular-5%");
                        state.ExecutedSteps.Add("regular-discount");
                        return true;
                    }))
                    .EndIf())
            .Else(f => f.Step("no-discount", async (state, ct) =>
            {
                state.Discount = 0;
                state.AppliedRules.Add("NoDiscount");
                state.ExecutedSteps.Add("no-discount");
                return true;
            }))
            .EndIf()
            .Step("calculate-final", async (state, ct) =>
            {
                state.FinalAmount = state.Amount - state.Discount;
                state.ExecutedSteps.Add("calculate-final");
                return true;
            })
            .Build();

        // Test VIP customer with high amount
        var vipState = new ComplexState { FlowId = "vip", Amount = 500m, CustomerType = "VIP" };
        var vipResult = await executor.ExecuteAsync(flow, vipState);

        vipResult.IsSuccess.Should().BeTrue();
        vipResult.State.Discount.Should().Be(100m); // 20% of 500
        vipResult.State.FinalAmount.Should().Be(400m);
        vipResult.State.AppliedRules.Should().Contain("VIP-20%");

        // Test low amount (no discount)
        var lowState = new ComplexState { FlowId = "low", Amount = 50m, CustomerType = "VIP" };
        var lowResult = await executor.ExecuteAsync(flow, lowState);

        lowResult.State.Discount.Should().Be(0);
        lowResult.State.AppliedRules.Should().Contain("NoDiscount");
    }

    [Fact]
    public async Task ComplexFlow_SwitchWithNestedLogic_RoutesCorrectly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<ComplexState>("switch-nested")
            .Switch(s => s.PaymentMethod)
                .Case("Card", f => f
                    .Step("card-validation", async (state, ct) =>
                    {
                        state.ExecutedSteps.Add("card-validation");
                        return true;
                    })
                    .If(s => s.Amount > 1000)
                        .Then(inner => inner.Step("card-3ds", async (state, ct) =>
                        {
                            state.ExecutedSteps.Add("card-3ds");
                            state.AppliedRules.Add("3DS-Required");
                            return true;
                        }))
                    .EndIf()
                    .Step("card-charge", async (state, ct) =>
                    {
                        state.PaymentProcessed = true;
                        state.ExecutedSteps.Add("card-charge");
                        return true;
                    }))
                .Case("PayPal", f => f
                    .Step("paypal-redirect", async (state, ct) =>
                    {
                        state.ExecutedSteps.Add("paypal-redirect");
                        return true;
                    })
                    .Step("paypal-confirm", async (state, ct) =>
                    {
                        state.PaymentProcessed = true;
                        state.ExecutedSteps.Add("paypal-confirm");
                        return true;
                    }))
                .Case("Crypto", f => f
                    .Step("crypto-wallet", async (state, ct) =>
                    {
                        state.ExecutedSteps.Add("crypto-wallet");
                        return true;
                    })
                    .Step("crypto-confirm", async (state, ct) =>
                    {
                        state.PaymentProcessed = true;
                        state.ExecutedSteps.Add("crypto-confirm");
                        return true;
                    }))
                .Default(f => f.Step("default-payment", async (state, ct) =>
                {
                    state.ExecutedSteps.Add("default-payment");
                    state.PaymentProcessed = true;
                    return true;
                }))
            .EndSwitch()
            .Build();

        // Test Card with high amount (requires 3DS)
        var cardState = new ComplexState { FlowId = "card", PaymentMethod = "Card", Amount = 2000m };
        var cardResult = await executor.ExecuteAsync(flow, cardState);

        cardResult.IsSuccess.Should().BeTrue();
        cardResult.State.PaymentProcessed.Should().BeTrue();
        cardResult.State.ExecutedSteps.Should().Contain("card-3ds");
        cardResult.State.AppliedRules.Should().Contain("3DS-Required");

        // Test PayPal
        var paypalState = new ComplexState { FlowId = "paypal", PaymentMethod = "PayPal", Amount = 100m };
        var paypalResult = await executor.ExecuteAsync(flow, paypalState);

        paypalResult.State.ExecutedSteps.Should().Contain("paypal-redirect");
        paypalResult.State.ExecutedSteps.Should().Contain("paypal-confirm");
    }

    [Fact]
    public async Task ComplexFlow_MultipleForEachWithConditions_ProcessesCorrectly()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<OrderItemsState>("foreach-complex")
            .Step("init", async (state, ct) =>
            {
                state.ExecutedSteps.Add("init");
                return true;
            })
            .ForEach(
                s => s.Items,
                (item, f) => f
                    .Step($"validate-{item.Sku}", async (state, ct) =>
                    {
                        state.ExecutedSteps.Add($"validate-{item.Sku}");
                        return true;
                    })
                    .If(s => item.Quantity > 5)
                        .Then(inner => inner.Step($"bulk-discount-{item.Sku}", async (state, ct) =>
                        {
                            state.ExecutedSteps.Add($"bulk-{item.Sku}");
                            state.TotalDiscount += item.Price * 0.1m * item.Quantity;
                            return true;
                        }))
                    .EndIf())
            .Step("finalize", async (state, ct) =>
            {
                state.ExecutedSteps.Add("finalize");
                state.FinalAmount = state.Items.Sum(i => i.Price * i.Quantity) - state.TotalDiscount;
                return true;
            })
            .Build();

        var state = new OrderItemsState
        {
            FlowId = "items-test",
            Items = new List<OrderItem>
            {
                new("SKU-001", 10, 50m),  // Bulk discount applies
                new("SKU-002", 2, 100m),  // No bulk discount
                new("SKU-003", 8, 25m)    // Bulk discount applies
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ExecutedSteps.Should().Contain("bulk-SKU-001");
        result.State.ExecutedSteps.Should().NotContain("bulk-SKU-002");
        result.State.ExecutedSteps.Should().Contain("bulk-SKU-003");
        result.State.TotalDiscount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ComplexFlow_WhileWithSwitch_ProcessesUntilCondition()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<RetryState>("while-switch")
            .While(s => s.Attempts < s.MaxAttempts && !s.Success)
                .Do(f => f
                    .Step("attempt", async (state, ct) =>
                    {
                        state.Attempts++;
                        state.ExecutedSteps.Add($"attempt-{state.Attempts}");
                        return true;
                    })
                    .Switch(s => s.Attempts)
                        .Case(1, inner => inner.Step("first-try", async (state, ct) =>
                        {
                            // First attempt fails
                            state.ExecutedSteps.Add("first-try-fail");
                            return true;
                        }))
                        .Case(2, inner => inner.Step("second-try", async (state, ct) =>
                        {
                            // Second attempt fails
                            state.ExecutedSteps.Add("second-try-fail");
                            return true;
                        }))
                        .Default(inner => inner.Step("final-try", async (state, ct) =>
                        {
                            // Third attempt succeeds
                            state.Success = true;
                            state.ExecutedSteps.Add("final-try-success");
                            return true;
                        }))
                    .EndSwitch())
            .EndWhile()
            .Build();

        var state = new RetryState { FlowId = "retry-test", MaxAttempts = 5 };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.Success.Should().BeTrue();
        result.State.Attempts.Should().Be(3);
        result.State.ExecutedSteps.Should().Contain("final-try-success");
    }

    [Fact]
    public async Task ComplexFlow_MultipleCompensationChains_ExecutesInOrder()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();
        var compensationOrder = new List<string>();

        var flow = FlowBuilder.Create<ComplexState>("multi-compensation")
            .Step("step-1", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-1");
                state.InventoryReserved = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                compensationOrder.Add("comp-1");
                state.InventoryReserved = false;
            })
            .Step("step-2", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-2");
                state.PaymentProcessed = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                compensationOrder.Add("comp-2");
                state.PaymentProcessed = false;
            })
            .Step("step-3", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-3");
                state.ShippingScheduled = true;
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                compensationOrder.Add("comp-3");
                state.ShippingScheduled = false;
            })
            .Step("step-4-fail", async (state, ct) =>
            {
                state.ExecutedSteps.Add("step-4");
                throw new InvalidOperationException("Final step failed");
            })
            .Build();

        var state = new ComplexState { FlowId = "comp-chain" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        compensationOrder.Should().HaveCount(3);
        // Verify reverse order
        compensationOrder[0].Should().Be("comp-3");
        compensationOrder[1].Should().Be("comp-2");
        compensationOrder[2].Should().Be("comp-1");
        // Verify state was rolled back
        result.State.InventoryReserved.Should().BeFalse();
        result.State.PaymentProcessed.Should().BeFalse();
        result.State.ShippingScheduled.Should().BeFalse();
    }

    public class OrderItemsState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public List<OrderItem> Items { get; set; } = new();
        public decimal TotalDiscount { get; set; }
        public decimal FinalAmount { get; set; }
        public List<string> ExecutedSteps { get; set; } = new();
    }

    public record OrderItem(string Sku, int Quantity, decimal Price);

    public class RetryState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int Attempts { get; set; }
        public int MaxAttempts { get; set; }
        public bool Success { get; set; }
        public List<string> ExecutedSteps { get; set; } = new();
    }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
