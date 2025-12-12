using Catga.Abstractions;
using Catga.Flow;
using Catga.Flow.Dsl;
using MemoryPack;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Messages;

// ============================================
// Base Command Classes (reduce boilerplate)
// ============================================

/// <summary>Base class for simple flow commands without response.</summary>
public abstract record BaseFlowCommand : IRequest
{
    public long MessageId => 0;
}

/// <summary>Base class for commands with response.</summary>
public abstract record BaseCommand : IRequest
{
    public long MessageId { get; init; }
}

/// <summary>Base class for commands with typed response.</summary>
public abstract record BaseCommand<TResponse> : IRequest<TResponse>
{
    public long MessageId { get; init; }
}

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
public partial class CreateOrderFlowState : BaseFlowState
{
    [FlowStateField]
    private string? _orderId;

    [FlowStateField]
    private decimal _totalAmount;

    [FlowStateField]
    private bool _stockReserved;

    public string? CustomerId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
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
public record SaveOrderFlowCommand(string OrderId, string CustomerId, List<OrderItem> Items, decimal TotalAmount) : BaseFlowCommand;
public record DeleteOrderFlowCommand(string OrderId) : BaseFlowCommand;
public record ReserveStockCommand(string OrderId, List<OrderItem> Items) : BaseFlowCommand;
public record ReleaseStockCommand(string OrderId) : BaseFlowCommand;
public record ConfirmOrderFlowCommand(string OrderId) : BaseFlowCommand;
public record MarkOrderFailedCommand(string OrderId) : BaseFlowCommand;

// ============================================
// ComprehensiveOrderFlow Commands & Events
// ============================================

// Approval & customer handling
public record RequireManagerApprovalCommand(string OrderId) : BaseCommand<bool>;
public record NotifyManagerCommand(string OrderId, string Message) : BaseCommand;
public record RequireSeniorStaffReviewCommand(string OrderId) : BaseCommand<bool>;
public record AutoApproveOrderCommand(string OrderId) : BaseCommand<bool>;
public record ApplyVIPDiscountCommand(string OrderId, decimal Rate) : BaseCommand<decimal>;
public record AssignPriorityShippingCommand(string OrderId) : BaseCommand<bool>;
public record ApplyStandardDiscountCommand(string OrderId, decimal Rate) : BaseCommand<decimal>;
public record SendWelcomeEmailCommand(string Email) : BaseCommand;
public record ApplyNewCustomerDiscountCommand(string OrderId, decimal Rate) : BaseCommand<decimal>;
public record LogUnknownCustomerTypeCommand(string OrderId) : BaseCommand;

// Inventory operations
public record CheckInventoryCommand(string ProductId, int Quantity) : BaseCommand<CheckInventoryResult>;
public record CheckInventoryResult(bool InStock, int AvailableQuantity);

public record ReserveInventoryCommand(string ProductId, int Quantity) : BaseCommand<ReserveInventoryResult>;
public record ReserveInventoryResult(string ReservationId);

// Payment providers
public record ProcessPaymentWithStripeCommand(string OrderId, decimal Amount) : BaseCommand<PaymentResult>;
public record ProcessPaymentWithPayPalCommand(string OrderId, decimal Amount) : BaseCommand<PaymentResult>;
public record ProcessPaymentWithSquareCommand(string OrderId, decimal Amount) : BaseCommand<PaymentResult>;
public record PaymentResult(string Provider, string TransactionId);

// Parallel operations
public record GenerateInvoiceCommand(string OrderId) : BaseCommand<string>;
public record UpdateCustomerLoyaltyPointsCommand(string CustomerId, decimal Amount) : BaseCommand<int>;
public record SendOrderConfirmationEmailCommand(string Email, string OrderId) : BaseCommand<bool>;

public record CreateShippingLabelCommand(string OrderId) : BaseCommand<string>;

// Warehouse allocation
public record CheckWarehouseCapacityCommand(string WarehouseId) : BaseCommand<int>;
public record AllocateItemToWarehouseCommand(string WarehouseId, string ProductId, int Quantity) : BaseCommand<AllocationResult>;
public record AllocationResult(int AllocatedQuantity);

// Fraud & risk
public record PerformFraudCheckCommand(string OrderId) : BaseCommand<double>;
public record FlagOrderForReviewCommand(string OrderId) : BaseCommand;
public record NotifySecurityTeamCommand(string OrderId, double FraudScore) : BaseCommand;
public record ReleaseInventoryReservationsCommand(Dictionary<string, string> ReservedItems) : BaseCommand;
public record RequireAdditionalVerificationCommand(string OrderId) : BaseCommand;

// Shipping scheduling
public record ScheduleExpressShippingCommand(string OrderId) : BaseCommand<DateTime>;
public record ScheduleStandardShippingCommand(string OrderId) : BaseCommand<DateTime>;
public record ScheduleEconomyShippingCommand(string OrderId) : BaseCommand<DateTime>;

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
public record UpdateOrderStatusCommand(string OrderId, OrderStatus Status) : BaseCommand;
public record RevertOrderStatusCommand(string OrderId, OrderStatus PreviousStatus) : BaseCommand;

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
