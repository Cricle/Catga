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

// ============================================
// ComprehensiveOrderFlow Commands & Events
// ============================================

// Approval & customer handling
public record RequireManagerApprovalCommand(string OrderId) : IRequest<bool>
{
    public long MessageId { get; init; }
}

public record NotifyManagerCommand(string OrderId, string Message) : IRequest
{
    public long MessageId { get; init; }
}

public record RequireSeniorStaffReviewCommand(string OrderId) : IRequest<bool>
{
    public long MessageId { get; init; }
}

public record AutoApproveOrderCommand(string OrderId) : IRequest<bool>
{
    public long MessageId { get; init; }
}

public record ApplyVIPDiscountCommand(string OrderId, decimal Rate) : IRequest<decimal>
{
    public long MessageId { get; init; }
}

public record AssignPriorityShippingCommand(string OrderId) : IRequest<bool>
{
    public long MessageId { get; init; }
}

public record ApplyStandardDiscountCommand(string OrderId, decimal Rate) : IRequest<decimal>
{
    public long MessageId { get; init; }
}

public record SendWelcomeEmailCommand(string Email) : IRequest
{
    public long MessageId { get; init; }
}

public record ApplyNewCustomerDiscountCommand(string OrderId, decimal Rate) : IRequest<decimal>
{
    public long MessageId { get; init; }
}

public record LogUnknownCustomerTypeCommand(string OrderId) : IRequest
{
    public long MessageId { get; init; }
}

// Inventory operations
public record CheckInventoryCommand(string ProductId, int Quantity) : IRequest<CheckInventoryResult>
{
    public long MessageId { get; init; }
}
public record CheckInventoryResult(bool InStock, int AvailableQuantity);

public record ReserveInventoryCommand(string ProductId, int Quantity) : IRequest<ReserveInventoryResult>
{
    public long MessageId { get; init; }
}
public record ReserveInventoryResult(string ReservationId);

// Payment providers
public record ProcessPaymentWithStripeCommand(string OrderId, decimal Amount) : IRequest<PaymentResult>
{
    public long MessageId { get; init; }
}

public record ProcessPaymentWithPayPalCommand(string OrderId, decimal Amount) : IRequest<PaymentResult>
{
    public long MessageId { get; init; }
}

public record ProcessPaymentWithSquareCommand(string OrderId, decimal Amount) : IRequest<PaymentResult>
{
    public long MessageId { get; init; }
}
public record PaymentResult(string Provider, string TransactionId);

// Parallel operations
public record GenerateInvoiceCommand(string OrderId) : IRequest<string>
{
    public long MessageId { get; init; }
}

public record UpdateCustomerLoyaltyPointsCommand(string CustomerId, decimal Amount) : IRequest<int>
{
    public long MessageId { get; init; }
}

public record SendOrderConfirmationEmailCommand(string Email, string OrderId) : IRequest<bool>
{
    public long MessageId { get; init; }
}

public record CreateShippingLabelCommand(string OrderId) : IRequest<string>
{
    public long MessageId { get; init; }
}

// Warehouse allocation
public record CheckWarehouseCapacityCommand(string WarehouseId) : IRequest<int>
{
    public long MessageId { get; init; }
}

public record AllocateItemToWarehouseCommand(string WarehouseId, string ProductId, int Quantity) : IRequest<AllocationResult>
{
    public long MessageId { get; init; }
}
public record AllocationResult(int AllocatedQuantity);

// Fraud & risk
public record PerformFraudCheckCommand(string OrderId) : IRequest<double>
{
    public long MessageId { get; init; }
}

public record FlagOrderForReviewCommand(string OrderId) : IRequest
{
    public long MessageId { get; init; }
}

public record NotifySecurityTeamCommand(string OrderId, double FraudScore) : IRequest
{
    public long MessageId { get; init; }
}

public record ReleaseInventoryReservationsCommand(Dictionary<string, string> ReservedItems) : IRequest
{
    public long MessageId { get; init; }
}

public record RequireAdditionalVerificationCommand(string OrderId) : IRequest
{
    public long MessageId { get; init; }
}

// Shipping scheduling
public record ScheduleExpressShippingCommand(string OrderId) : IRequest<DateTime>
{
    public long MessageId { get; init; }
}

public record ScheduleStandardShippingCommand(string OrderId) : IRequest<DateTime>
{
    public long MessageId { get; init; }
}

public record ScheduleEconomyShippingCommand(string OrderId) : IRequest<DateTime>
{
    public long MessageId { get; init; }
}

// Downstream notification
public class OrderProcessedEvent : IEvent
{
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public DateTime EstimatedDelivery { get; set; }
    public long MessageId { get; init; }
}

// Order status & metrics
public record UpdateOrderStatusCommand(string OrderId, OrderStatus Status) : IRequest
{
    public long MessageId { get; init; }
}

public record RevertOrderStatusCommand(string OrderId, OrderStatus PreviousStatus) : IRequest
{
    public long MessageId { get; init; }
}

public class RecordOrderMetricsCommand : IRequest
{
    public string OrderId { get; set; } = string.Empty;
    public TimeSpan ProcessingTime { get; set; }
    public int ItemsProcessed { get; set; }
    public int ItemsFailed { get; set; }
    public double FraudScore { get; set; }
    public string PaymentProvider { get; set; } = string.Empty;
    public long MessageId { get; init; }
}

// ============================================
// Additional flows in Program.FlowDsl
// ============================================

// PaymentProcessingFlow
public record ProcessPaymentCommand(string PaymentId, decimal Amount) : IRequest
{
    public long MessageId { get; init; }
}

public record RefundPaymentCommand(string PaymentId) : IRequest
{
    public long MessageId { get; init; }
}

// ShippingOrchestrationFlow quotes
public record GetQuoteFromFedExCommand(string ShipmentId) : IRequest<ShippingQuote>
{
    public long MessageId { get; init; }
}

public record GetQuoteFromUPSCommand(string ShipmentId) : IRequest<ShippingQuote>
{
    public long MessageId { get; init; }
}

public record GetQuoteFromDHLCommand(string ShipmentId) : IRequest<ShippingQuote>
{
    public long MessageId { get; init; }
}
public record ShippingQuote(string Carrier, decimal Price);

// InventoryManagementFlow stock level
public record CheckStockLevelCommand(string ProductId) : IRequest<int>
{
    public long MessageId { get; init; }
}

// CustomerOnboardingFlow
public record ValidateCustomerDataCommand(string CustomerId) : IRequest
{
    public long MessageId { get; init; }
}

public record CreateCustomerAccountCommand(string CustomerId) : IRequest
{
    public long MessageId { get; init; }
}

public record SendWelcomePackageCommand(string CustomerId) : IRequest
{
    public long MessageId { get; init; }
}

public class CustomerOnboardedEvent : IEvent
{
    public string CustomerId { get; set; } = string.Empty;
    public long MessageId { get; init; }
}
