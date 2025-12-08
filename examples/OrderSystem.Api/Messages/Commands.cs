using Catga.Abstractions;
using Catga.Flow.Dsl;
using MemoryPack;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Messages;

// ============================================
// Commands & Queries
// ============================================

/// <summary>Simple create order command.</summary>
[MemoryPackable]
public partial record CreateOrderCommand(string CustomerId, List<OrderItem> Items) : IRequest<OrderCreatedResult>;

/// <summary>Create order using Flow pattern with automatic compensation.</summary>
[MemoryPackable]
public partial record CreateOrderFlowCommand(string CustomerId, List<OrderItem> Items) : IRequest<OrderCreatedResult>;

[MemoryPackable]
public partial record OrderCreatedResult(string OrderId, decimal TotalAmount, DateTime CreatedAt);

[MemoryPackable]
public partial record CancelOrderCommand(string OrderId, string? Reason = null) : IRequest;

[MemoryPackable]
public partial record GetOrderQuery(string OrderId) : IRequest<Order?>;

[MemoryPackable]
public partial record GetUserOrdersQuery(string CustomerId) : IRequest<List<Order>>;

// ============================================
// Events (for Pub/Sub)
// ============================================

/// <summary>Published when an order is created.</summary>
[MemoryPackable]
public partial record OrderCreatedEvent(string OrderId, string CustomerId, decimal TotalAmount, DateTime CreatedAt) : IEvent;

/// <summary>Published when an order is cancelled.</summary>
[MemoryPackable]
public partial record OrderCancelledEvent(string OrderId, string? Reason, DateTime CancelledAt) : IEvent;

/// <summary>Published when an order is confirmed.</summary>
[MemoryPackable]
public partial record OrderConfirmedEvent(string OrderId, DateTime ConfirmedAt) : IEvent;

// ============================================
// Flow DSL (Saga pattern with compensation)
// ============================================

/// <summary>Flow state for order creation saga.</summary>
[FlowState]
public partial class CreateOrderFlowState
{
    public string? OrderId { get; set; }
    public decimal TotalAmount { get; set; }
    public bool StockReserved { get; set; }
    [FlowStateIgnore] public string? CustomerId { get; set; }
    [FlowStateIgnore] public List<OrderItem> Items { get; set; } = [];
}

/// <summary>Flow configuration: SaveOrder -> ReserveStock -> ConfirmOrder -> PublishEvent</summary>
public class CreateOrderFlowConfig : FlowConfig<CreateOrderFlowState>
{
    protected override void Configure(IFlowBuilder<CreateOrderFlowState> flow)
    {
        flow.Name("create-order");
        flow.Timeout(TimeSpan.FromMinutes(5));
        flow.Retry(3).ForTags("critical");

        // Step 1: Save order (compensate: delete)
        flow.Send(s => new SaveOrderFlowCommand(s.OrderId!, s.CustomerId!, s.Items, s.TotalAmount))
            .IfFail(s => new DeleteOrderFlowCommand(s.OrderId!))
            .Tag("persistence");

        // Step 2: Reserve stock (compensate: release)
        flow.Send(s => new ReserveStockCommand(s.OrderId!, s.Items))
            .IfFail(s => new ReleaseStockCommand(s.OrderId!))
            .Tag("inventory", "critical");

        // Step 3: Confirm order (compensate: mark failed)
        flow.Send(s => new ConfirmOrderFlowCommand(s.OrderId!))
            .IfFail(s => new MarkOrderFailedCommand(s.OrderId!))
            .Tag("persistence");

        // Step 4: Publish event
        flow.Publish(s => new OrderConfirmedEvent(s.OrderId!, DateTime.UtcNow))
            .Tag("notification");
    }
}

// Flow-specific commands (internal)
public record SaveOrderFlowCommand(string OrderId, string CustomerId, List<OrderItem> Items, decimal TotalAmount) : IRequest { public long MessageId => 0; }
public record DeleteOrderFlowCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record ReserveStockCommand(string OrderId, List<OrderItem> Items) : IRequest { public long MessageId => 0; }
public record ReleaseStockCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record ConfirmOrderFlowCommand(string OrderId) : IRequest { public long MessageId => 0; }
public record MarkOrderFailedCommand(string OrderId) : IRequest { public long MessageId => 0; }

