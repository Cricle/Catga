using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// State machine workflow scenarios.
/// Tests state transitions, guards, and complex state management.
/// </summary>
public class StateMachineFlowTests
{
    #region Test State

    public class OrderStateMachine : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public OrderState CurrentState { get; set; } = OrderState.Draft;
        public List<StateTransition> TransitionHistory { get; set; } = new();
        public string? LastError { get; set; }

        // Order data
        public bool IsPaid { get; set; }
        public bool IsShipped { get; set; }
        public bool IsDelivered { get; set; }
        public bool IsCancelled { get; set; }
    }

    public enum OrderState
    {
        Draft,
        Submitted,
        Confirmed,
        Paid,
        Processing,
        Shipped,
        Delivered,
        Cancelled,
        Refunded
    }

    public record StateTransition(OrderState From, OrderState To, DateTime At, string Reason);

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
    public async Task StateMachine_HappyPath_CompletesAllTransitions()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = CreateOrderStateMachineFlow();
        var state = new OrderStateMachine
        {
            FlowId = "order-sm",
            OrderId = "ORD-001",
            CurrentState = OrderState.Draft
        };

        // Simulate actions for happy path
        state.IsPaid = true;

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CurrentState.Should().Be(OrderState.Delivered);
        result.State.TransitionHistory.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task StateMachine_CancelFromDraft_TransitionsToCancel()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<OrderStateMachine>("cancel-flow")
            .If(s => s.CurrentState == OrderState.Draft || s.CurrentState == OrderState.Submitted)
                .Then(f => f.Step("cancel", async (state, ct) =>
                {
                    var from = state.CurrentState;
                    state.CurrentState = OrderState.Cancelled;
                    state.IsCancelled = true;
                    state.TransitionHistory.Add(new StateTransition(from, OrderState.Cancelled, DateTime.UtcNow, "User requested"));
                    return true;
                }))
            .Else(f => f.Step("cannot-cancel", async (state, ct) =>
            {
                state.LastError = "Cannot cancel order in current state";
                return false;
            }))
            .EndIf()
            .Build();

        var state = new OrderStateMachine { FlowId = "cancel-test", CurrentState = OrderState.Draft };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CurrentState.Should().Be(OrderState.Cancelled);
        result.State.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task StateMachine_CancelAfterShip_Fails()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<OrderStateMachine>("cancel-shipped")
            .If(s => s.CurrentState == OrderState.Draft || s.CurrentState == OrderState.Submitted)
                .Then(f => f.Step("cancel", async (state, ct) =>
                {
                    state.CurrentState = OrderState.Cancelled;
                    return true;
                }))
            .Else(f => f.Step("cannot-cancel", async (state, ct) =>
            {
                state.LastError = $"Cannot cancel order in {state.CurrentState} state";
                return false;
            }))
            .EndIf()
            .Build();

        var state = new OrderStateMachine { FlowId = "cancel-shipped-test", CurrentState = OrderState.Shipped };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.State.CurrentState.Should().Be(OrderState.Shipped); // Unchanged
        result.State.LastError.Should().Contain("Cannot cancel");
    }

    [Fact]
    public async Task StateMachine_RefundAfterDelivery_AllowedWithConditions()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<RefundableState>("refund-flow")
            .If(s => s.CurrentState == OrderState.Delivered && s.DaysSinceDelivery <= 30)
                .Then(f => f.Step("process-refund", async (state, ct) =>
                {
                    state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Refunded, DateTime.UtcNow, "Refund requested"));
                    state.CurrentState = OrderState.Refunded;
                    state.IsRefunded = true;
                    return true;
                }))
            .Else(f => f.Step("refund-denied", async (state, ct) =>
            {
                state.LastError = "Refund window expired or order not delivered";
                return false;
            }))
            .EndIf()
            .Build();

        // Test within refund window
        var validState = new RefundableState { FlowId = "refund-valid", CurrentState = OrderState.Delivered, DaysSinceDelivery = 15 };
        var validResult = await executor.ExecuteAsync(flow, validState);

        validResult.IsSuccess.Should().BeTrue();
        validResult.State.CurrentState.Should().Be(OrderState.Refunded);
        validResult.State.IsRefunded.Should().BeTrue();

        // Test outside refund window
        var expiredState = new RefundableState { FlowId = "refund-expired", CurrentState = OrderState.Delivered, DaysSinceDelivery = 45 };
        var expiredResult = await executor.ExecuteAsync(flow, expiredState);

        expiredResult.IsSuccess.Should().BeFalse();
        expiredResult.State.CurrentState.Should().Be(OrderState.Delivered);
    }

    [Fact]
    public async Task StateMachine_ComplexTransitions_TrackHistory()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<OrderStateMachine>("complex-transitions")
            .Step("submit", async (state, ct) =>
            {
                if (state.CurrentState != OrderState.Draft) return false;
                state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Submitted, DateTime.UtcNow, "Order submitted"));
                state.CurrentState = OrderState.Submitted;
                return true;
            })
            .Step("confirm", async (state, ct) =>
            {
                if (state.CurrentState != OrderState.Submitted) return false;
                state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Confirmed, DateTime.UtcNow, "Order confirmed"));
                state.CurrentState = OrderState.Confirmed;
                return true;
            })
            .Step("pay", async (state, ct) =>
            {
                if (state.CurrentState != OrderState.Confirmed) return false;
                state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Paid, DateTime.UtcNow, "Payment received"));
                state.CurrentState = OrderState.Paid;
                state.IsPaid = true;
                return true;
            })
            .Step("process", async (state, ct) =>
            {
                if (state.CurrentState != OrderState.Paid) return false;
                state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Processing, DateTime.UtcNow, "Processing started"));
                state.CurrentState = OrderState.Processing;
                return true;
            })
            .Build();

        var state = new OrderStateMachine { FlowId = "complex-test", CurrentState = OrderState.Draft };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CurrentState.Should().Be(OrderState.Processing);
        result.State.TransitionHistory.Should().HaveCount(4);
        result.State.TransitionHistory.Select(t => t.To).Should().ContainInOrder(
            OrderState.Submitted,
            OrderState.Confirmed,
            OrderState.Paid,
            OrderState.Processing);
    }

    [Fact]
    public async Task StateMachine_WithGuardConditions_EnforcesRules()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<InventoryStateMachine>("inventory-sm")
            .Switch(s => s.CurrentState)
                .Case(InventoryState.Available, f => f
                    .If(s => s.RequestedQuantity <= s.AvailableQuantity)
                        .Then(inner => inner.Step("reserve", async (state, ct) =>
                        {
                            state.AvailableQuantity -= state.RequestedQuantity;
                            state.ReservedQuantity += state.RequestedQuantity;
                            state.CurrentState = InventoryState.Reserved;
                            state.TransitionLog.Add("Reserved");
                            return true;
                        }))
                        .Else(inner => inner.Step("insufficient", async (state, ct) =>
                        {
                            state.LastError = "Insufficient inventory";
                            return false;
                        }))
                    .EndIf())
                .Case(InventoryState.Reserved, f => f
                    .Step("commit", async (state, ct) =>
                    {
                        state.ReservedQuantity -= state.RequestedQuantity;
                        state.CurrentState = InventoryState.Committed;
                        state.TransitionLog.Add("Committed");
                        return true;
                    }))
                .Default(f => f.Step("no-action", async (state, ct) =>
                {
                    state.TransitionLog.Add("No action");
                    return true;
                }))
            .EndSwitch()
            .Build();

        // Test successful reservation
        var availableState = new InventoryStateMachine
        {
            FlowId = "inv-test",
            CurrentState = InventoryState.Available,
            AvailableQuantity = 100,
            RequestedQuantity = 50
        };

        var result = await executor.ExecuteAsync(flow, availableState);

        result.IsSuccess.Should().BeTrue();
        result.State.CurrentState.Should().Be(InventoryState.Reserved);
        result.State.AvailableQuantity.Should().Be(50);
        result.State.ReservedQuantity.Should().Be(50);
    }

    private IFlow<OrderStateMachine> CreateOrderStateMachineFlow()
    {
        return FlowBuilder.Create<OrderStateMachine>("order-state-machine")
            .Step("submit", async (state, ct) =>
            {
                if (state.CurrentState == OrderState.Draft)
                {
                    state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Submitted, DateTime.UtcNow, "Submitted"));
                    state.CurrentState = OrderState.Submitted;
                }
                return true;
            })
            .Step("confirm", async (state, ct) =>
            {
                if (state.CurrentState == OrderState.Submitted)
                {
                    state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Confirmed, DateTime.UtcNow, "Confirmed"));
                    state.CurrentState = OrderState.Confirmed;
                }
                return true;
            })
            .If(s => s.IsPaid)
                .Then(f => f
                    .Step("mark-paid", async (state, ct) =>
                    {
                        state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Paid, DateTime.UtcNow, "Paid"));
                        state.CurrentState = OrderState.Paid;
                        return true;
                    })
                    .Step("process", async (state, ct) =>
                    {
                        state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Processing, DateTime.UtcNow, "Processing"));
                        state.CurrentState = OrderState.Processing;
                        return true;
                    })
                    .Step("ship", async (state, ct) =>
                    {
                        state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Shipped, DateTime.UtcNow, "Shipped"));
                        state.CurrentState = OrderState.Shipped;
                        state.IsShipped = true;
                        return true;
                    })
                    .Step("deliver", async (state, ct) =>
                    {
                        state.TransitionHistory.Add(new StateTransition(state.CurrentState, OrderState.Delivered, DateTime.UtcNow, "Delivered"));
                        state.CurrentState = OrderState.Delivered;
                        state.IsDelivered = true;
                        return true;
                    }))
            .EndIf()
            .Build();
    }

    public class RefundableState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public OrderState CurrentState { get; set; }
        public int DaysSinceDelivery { get; set; }
        public bool IsRefunded { get; set; }
        public string? LastError { get; set; }
        public List<StateTransition> TransitionHistory { get; set; } = new();
    }

    public class InventoryStateMachine : IFlowState
    {
        public string FlowId { get; set; } = "";
        public InventoryState CurrentState { get; set; } = InventoryState.Available;
        public int AvailableQuantity { get; set; }
        public int ReservedQuantity { get; set; }
        public int RequestedQuantity { get; set; }
        public string? LastError { get; set; }
        public List<string> TransitionLog { get; set; } = new();
    }

    public enum InventoryState { Available, Reserved, Committed, Released }

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
