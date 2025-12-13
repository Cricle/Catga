using Catga.Abstractions;
using Catga.Flow.Dsl;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Flow.Scenarios;

/// <summary>
/// Event-driven workflow scenario tests.
/// Tests event publishing, subscription, and event-based state transitions.
/// </summary>
public class EventDrivenFlowTests
{
    #region Test State

    public class EventDrivenState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public List<DomainEvent> PublishedEvents { get; set; } = new();
        public List<string> ProcessedEvents { get; set; } = new();
        public string CurrentStatus { get; set; } = "Created";
        public Dictionary<string, object> EventData { get; set; } = new();
    }

    public record DomainEvent(string Type, string AggregateId, DateTime OccurredAt, Dictionary<string, object> Data);

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
    public async Task EventDriven_PublishesEventsOnStateChange()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EventDrivenState>("event-publish")
            .Step("create-order", async (state, ct) =>
            {
                state.CurrentStatus = "Created";
                state.PublishedEvents.Add(new DomainEvent(
                    "OrderCreated",
                    state.OrderId,
                    DateTime.UtcNow,
                    new() { ["orderId"] = state.OrderId }));
                return true;
            })
            .Step("confirm-order", async (state, ct) =>
            {
                state.CurrentStatus = "Confirmed";
                state.PublishedEvents.Add(new DomainEvent(
                    "OrderConfirmed",
                    state.OrderId,
                    DateTime.UtcNow,
                    new() { ["orderId"] = state.OrderId, ["confirmedAt"] = DateTime.UtcNow }));
                return true;
            })
            .Step("ship-order", async (state, ct) =>
            {
                state.CurrentStatus = "Shipped";
                state.PublishedEvents.Add(new DomainEvent(
                    "OrderShipped",
                    state.OrderId,
                    DateTime.UtcNow,
                    new() { ["orderId"] = state.OrderId, ["trackingNumber"] = "TRK-12345" }));
                return true;
            })
            .Build();

        var state = new EventDrivenState { FlowId = "event-test", OrderId = "ORD-001" };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.PublishedEvents.Should().HaveCount(3);
        result.State.PublishedEvents.Select(e => e.Type).Should()
            .ContainInOrder("OrderCreated", "OrderConfirmed", "OrderShipped");
    }

    [Fact]
    public async Task EventDriven_ReactsToEventConditions()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EventReactionState>("event-reaction")
            .Step("receive-event", async (state, ct) =>
            {
                state.ProcessedEvents.Add(state.IncomingEvent.Type);
                return true;
            })
            .Switch(s => s.IncomingEvent.Type)
                .Case("PaymentReceived", f => f
                    .Step("process-payment", async (state, ct) =>
                    {
                        state.CurrentStatus = "PaymentProcessed";
                        state.ResponseEvents.Add(new DomainEvent(
                            "PaymentProcessed",
                            state.OrderId,
                            DateTime.UtcNow,
                            new() { ["amount"] = state.IncomingEvent.Data["amount"] }));
                        return true;
                    }))
                .Case("PaymentFailed", f => f
                    .Step("handle-failure", async (state, ct) =>
                    {
                        state.CurrentStatus = "PaymentFailed";
                        state.ResponseEvents.Add(new DomainEvent(
                            "OrderCancelled",
                            state.OrderId,
                            DateTime.UtcNow,
                            new() { ["reason"] = "Payment failed" }));
                        return true;
                    }))
                .Case("RefundRequested", f => f
                    .Step("process-refund", async (state, ct) =>
                    {
                        state.CurrentStatus = "RefundProcessing";
                        state.ResponseEvents.Add(new DomainEvent(
                            "RefundInitiated",
                            state.OrderId,
                            DateTime.UtcNow,
                            new() { ["refundId"] = Guid.NewGuid().ToString() }));
                        return true;
                    }))
                .Default(f => f.Step("unknown-event", async (state, ct) =>
                {
                    state.CurrentStatus = "UnknownEvent";
                    return true;
                }))
            .EndSwitch()
            .Build();

        // Test PaymentReceived
        var paymentState = new EventReactionState
        {
            FlowId = "reaction-1",
            OrderId = "ORD-001",
            IncomingEvent = new DomainEvent("PaymentReceived", "ORD-001", DateTime.UtcNow, new() { ["amount"] = 100m })
        };

        var paymentResult = await executor.ExecuteAsync(flow, paymentState);

        paymentResult.IsSuccess.Should().BeTrue();
        paymentResult.State.CurrentStatus.Should().Be("PaymentProcessed");
        paymentResult.State.ResponseEvents.Should().Contain(e => e.Type == "PaymentProcessed");
    }

    [Fact]
    public async Task EventDriven_ChainsMultipleEventHandlers()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<EventChainState>("event-chain")
            .ForEach(
                s => s.EventQueue,
                (evt, f) => f
                    .Step($"process-{evt.Type}", async (state, ct) =>
                    {
                        state.ProcessedEvents.Add(evt.Type);

                        // Generate follow-up events
                        if (evt.Type == "OrderCreated")
                        {
                            state.GeneratedEvents.Add(new DomainEvent("InventoryReserved", state.OrderId, DateTime.UtcNow, new()));
                        }
                        else if (evt.Type == "InventoryReserved")
                        {
                            state.GeneratedEvents.Add(new DomainEvent("PaymentRequested", state.OrderId, DateTime.UtcNow, new()));
                        }
                        else if (evt.Type == "PaymentRequested")
                        {
                            state.GeneratedEvents.Add(new DomainEvent("OrderConfirmed", state.OrderId, DateTime.UtcNow, new()));
                        }

                        return true;
                    }))
            .Build();

        var state = new EventChainState
        {
            FlowId = "chain-test",
            OrderId = "ORD-001",
            EventQueue = new List<DomainEvent>
            {
                new("OrderCreated", "ORD-001", DateTime.UtcNow, new()),
                new("InventoryReserved", "ORD-001", DateTime.UtcNow, new()),
                new("PaymentRequested", "ORD-001", DateTime.UtcNow, new())
            }
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.ProcessedEvents.Should().HaveCount(3);
        result.State.GeneratedEvents.Should().HaveCount(3);
    }

    [Fact]
    public async Task EventDriven_SagaPattern_CoordinatesServices()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var flow = FlowBuilder.Create<SagaState>("saga-flow")
            // Step 1: Reserve inventory
            .Step("reserve-inventory", async (state, ct) =>
            {
                state.SagaLog.Add("InventoryReserved");
                state.Events.Add(new DomainEvent("InventoryReserved", state.OrderId, DateTime.UtcNow, new()));
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.SagaLog.Add("InventoryReleased");
                state.Events.Add(new DomainEvent("InventoryReleased", state.OrderId, DateTime.UtcNow, new()));
            })
            // Step 2: Process payment
            .Step("process-payment", async (state, ct) =>
            {
                if (state.ShouldFailPayment)
                {
                    throw new InvalidOperationException("Payment declined");
                }
                state.SagaLog.Add("PaymentProcessed");
                state.Events.Add(new DomainEvent("PaymentProcessed", state.OrderId, DateTime.UtcNow, new()));
                return true;
            })
            .WithCompensation(async (state, ct) =>
            {
                state.SagaLog.Add("PaymentRefunded");
                state.Events.Add(new DomainEvent("PaymentRefunded", state.OrderId, DateTime.UtcNow, new()));
            })
            // Step 3: Create shipment
            .Step("create-shipment", async (state, ct) =>
            {
                state.SagaLog.Add("ShipmentCreated");
                state.Events.Add(new DomainEvent("ShipmentCreated", state.OrderId, DateTime.UtcNow, new()));
                return true;
            })
            .Build();

        // Test success path
        var successState = new SagaState { FlowId = "saga-success", OrderId = "ORD-001" };
        var successResult = await executor.ExecuteAsync(flow, successState);

        successResult.IsSuccess.Should().BeTrue();
        successResult.State.SagaLog.Should().ContainInOrder("InventoryReserved", "PaymentProcessed", "ShipmentCreated");

        // Test failure path with compensation
        var failState = new SagaState { FlowId = "saga-fail", OrderId = "ORD-002", ShouldFailPayment = true };
        var failResult = await executor.ExecuteAsync(flow, failState);

        failResult.IsSuccess.Should().BeFalse();
        failResult.State.SagaLog.Should().Contain("InventoryReleased"); // Compensation executed
    }

    [Fact]
    public async Task EventDriven_EventSourcingPattern_RebuildsState()
    {
        // Arrange
        var sp = CreateServices();
        var executor = sp.GetRequiredService<IDslFlowExecutor>();

        var events = new List<DomainEvent>
        {
            new("OrderCreated", "ORD-001", DateTime.UtcNow.AddMinutes(-10), new() { ["amount"] = 100m }),
            new("ItemAdded", "ORD-001", DateTime.UtcNow.AddMinutes(-8), new() { ["itemId"] = "ITEM-1", ["price"] = 50m }),
            new("ItemAdded", "ORD-001", DateTime.UtcNow.AddMinutes(-6), new() { ["itemId"] = "ITEM-2", ["price"] = 30m }),
            new("DiscountApplied", "ORD-001", DateTime.UtcNow.AddMinutes(-4), new() { ["discount"] = 10m }),
            new("OrderConfirmed", "ORD-001", DateTime.UtcNow.AddMinutes(-2), new())
        };

        var flow = FlowBuilder.Create<EventSourcedState>("event-sourced")
            .ForEach(
                s => s.EventHistory,
                (evt, f) => f.Step($"apply-{evt.Type}", async (state, ct) =>
                {
                    switch (evt.Type)
                    {
                        case "OrderCreated":
                            state.TotalAmount = Convert.ToDecimal(evt.Data["amount"]);
                            state.Status = "Created";
                            break;
                        case "ItemAdded":
                            state.TotalAmount += Convert.ToDecimal(evt.Data["price"]);
                            state.ItemCount++;
                            break;
                        case "DiscountApplied":
                            state.TotalAmount -= Convert.ToDecimal(evt.Data["discount"]);
                            break;
                        case "OrderConfirmed":
                            state.Status = "Confirmed";
                            break;
                    }
                    state.AppliedEvents.Add(evt.Type);
                    return true;
                }))
            .Build();

        var state = new EventSourcedState
        {
            FlowId = "es-test",
            OrderId = "ORD-001",
            EventHistory = events
        };

        // Act
        var result = await executor.ExecuteAsync(flow, state);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.TotalAmount.Should().Be(170m); // 100 + 50 + 30 - 10
        result.State.ItemCount.Should().Be(2);
        result.State.Status.Should().Be("Confirmed");
        result.State.AppliedEvents.Should().HaveCount(5);
    }

    #region Supporting Types

    public class EventReactionState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public DomainEvent IncomingEvent { get; set; } = null!;
        public List<string> ProcessedEvents { get; set; } = new();
        public List<DomainEvent> ResponseEvents { get; set; } = new();
        public string CurrentStatus { get; set; } = "";
    }

    public class EventChainState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public List<DomainEvent> EventQueue { get; set; } = new();
        public List<string> ProcessedEvents { get; set; } = new();
        public List<DomainEvent> GeneratedEvents { get; set; } = new();
    }

    public class SagaState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public bool ShouldFailPayment { get; set; }
        public List<string> SagaLog { get; set; } = new();
        public List<DomainEvent> Events { get; set; } = new();
    }

    public class EventSourcedState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public string OrderId { get; set; } = "";
        public List<DomainEvent> EventHistory { get; set; } = new();
        public List<string> AppliedEvents { get; set; } = new();
        public decimal TotalAmount { get; set; }
        public int ItemCount { get; set; }
        public string Status { get; set; } = "";
    }

    #endregion

    private class TestSerializer : IMessageSerializer
    {
        public byte[] Serialize<T>(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
        public byte[] Serialize(object value, Type type) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, type);
        public T? Deserialize<T>(byte[] data) => System.Text.Json.JsonSerializer.Deserialize<T>(data);
        public object? Deserialize(byte[] data, Type type) => System.Text.Json.JsonSerializer.Deserialize(data, type);
    }
}
