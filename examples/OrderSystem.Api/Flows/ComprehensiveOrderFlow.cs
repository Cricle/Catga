using Catga.Flow.Dsl;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Flows;

/// <summary>
/// Comprehensive order processing flow demonstrating all Flow DSL features.
/// This flow showcases every capability of the Catga Flow DSL system.
/// </summary>
public class ComprehensiveOrderFlow : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow.Name("comprehensive-order-processing");

        // 1. Initial validation with If/ElseIf/Else branching
        flow.If(s => s.Order.TotalAmount > 10000)
                .Send(s => new RequireManagerApprovalCommand(s.OrderId))
                .Into(s => s.RequiresApproval)
                .If(s => s.RequiresApproval)
                    .Send(s => new NotifyManagerCommand(s.OrderId, "High-value order needs approval"))
                .EndIf()
            .ElseIf(s => s.Order.TotalAmount > 5000)
                .Send(s => new RequireSeniorStaffReviewCommand(s.OrderId))
                .Into(s => s.RequiresReview)
            .Else()
                .Send(s => new AutoApproveOrderCommand(s.OrderId))
                .Into(s => s.IsApproved)
            .EndIf();

        // 2. Customer validation with Switch/Case
        flow.Switch(s => s.Order.CustomerType)
            .Case(CustomerType.VIP, vip =>
            {
                vip.Send(s => new ApplyVIPDiscountCommand(s.OrderId, 0.20m))
                   .Into(s => s.Order.DiscountAmount);

                vip.Send(s => new AssignPriorityShippingCommand(s.OrderId));
            })
            .Case(CustomerType.Regular, regular =>
            {
                regular.Send(s => new ApplyStandardDiscountCommand(s.OrderId, 0.05m))
                       .Into(s => s.Order.DiscountAmount);
            })
            .Case(CustomerType.New, newCustomer =>
            {
                newCustomer.Send(s => new SendWelcomeEmailCommand(s.Order.CustomerEmail))
                           .Send(s => new ApplyNewCustomerDiscountCommand(s.OrderId, 0.10m))
                           .Into(s => s.Order.DiscountAmount);
            })
            .Default(unknown =>
            {
                unknown.Send(s => new LogUnknownCustomerTypeCommand(s.OrderId));
            })
            .EndSwitch();

        // 3. Parallel inventory check with ForEach
        flow.ForEach(s => s.Order.Items)
            .WithParallelism(5) // Process up to 5 items simultaneously
            .Configure((item, f) =>
            {
                f.Send(s => new CheckInventoryCommand(item.ProductId, item.Quantity));
                f.Send(s => new ReserveInventoryCommand(item.ProductId, item.Quantity));
            })
            .OnItemSuccess((state, item, result) =>
            {
                switch (result)
                {
                    case CheckInventoryResult inv:
                        if (!inv.InStock)
                        {
                            state.OutOfStockItems.Add(item.ProductId);
                        }
                        else
                        {
                            state.AvailableQuantities[item.ProductId] = inv.AvailableQuantity;
                        }
                        break;
                    case ReserveInventoryResult res:
                        state.ReservedItems[item.ProductId] = res.ReservationId;
                        break;
                }

                state.ProcessedItems.Add(item.ProductId);
                state.ProcessingProgress = (decimal)state.ProcessedItems.Count / state.Order.Items.Count * 100;
            })
            .OnItemFail((state, item, error) =>
            {
                state.FailedItems.Add(item.ProductId);
                state.Errors.Add($"Failed to process item {item.ProductId}: {error}");
            })
            .ContinueOnFailure() // Continue processing even if some items fail
            .EndForEach();

        // 4. Payment processing with multiple providers (WhenAny)
        flow.WhenAny<PaymentResult>(
            s => new ProcessPaymentWithStripeCommand(s.OrderId, s.Order.TotalAmount),
            s => new ProcessPaymentWithPayPalCommand(s.OrderId, s.Order.TotalAmount),
            s => new ProcessPaymentWithSquareCommand(s.OrderId, s.Order.TotalAmount)
        )
        .Into(s => s.PaymentResult!);

        // 5. Parallel operations (sequential in this sample)
        flow.Send(s => new GenerateInvoiceCommand(s.OrderId))
            .Into(s => s.InvoiceNumber);

        flow.Send(s => new UpdateCustomerLoyaltyPointsCommand(s.Order.CustomerId, s.Order.TotalAmount))
            .Into(s => s.LoyaltyPointsAwarded);

        flow.Send(s => new SendOrderConfirmationEmailCommand(s.Order.CustomerEmail, s.OrderId))
            .Into(s => s.EmailSent);

        flow.Send(s => new CreateShippingLabelCommand(s.OrderId))
            .Into(s => s.ShippingLabel);

        // 6. Nested ForEach for multi-warehouse fulfillment
        flow.ForEach(s => s.Warehouses)
            .WithBatchSize(10) // Process in batches
            .Configure((warehouse, f) =>
            {
                f.Send(s => new CheckWarehouseCapacityCommand(warehouse.Id));

                // Nested ForEach for items per warehouse
                f.ForEach(s => s.Order.Items.Where(i => warehouse.HasProduct(i.ProductId)))
                    .Configure((item, itemFlow) =>
                    {
                        itemFlow.Send(s => new AllocateItemToWarehouseCommand(warehouse.Id, item.ProductId, item.Quantity));
                    })
                    .OnItemSuccess((state, item, result) =>
                    {
                        if (result is AllocationResult alloc)
                        {
                            state.WarehouseAllocations.Add(new WarehouseAllocation
                            {
                                WarehouseId = warehouse.Id,
                                ProductId = item.ProductId,
                                Quantity = alloc.AllocatedQuantity
                            });
                        }
                    })
                    .EndForEach();
            })
            .OnItemSuccess((state, warehouse, result) =>
            {
                if (result is int capacity)
                {
                    state.WarehouseCapacities[warehouse.Id] = capacity;
                }
            })
            .EndForEach();

        // 7. Risk assessment with complex conditions
        flow.If(s => s.Order.TotalAmount > 1000 && s.Order.CustomerType == CustomerType.New)
                .Send(s => new PerformFraudCheckCommand(s.OrderId))
                .Into(s => s.FraudScore)
                .If(s => s.FraudScore > 0.7)
                    .Send(s => new FlagOrderForReviewCommand(s.OrderId))
                    .Send(s => new NotifySecurityTeamCommand(s.OrderId, s.FraudScore))
                .ElseIf(s => s.FraudScore > 0.4)
                    .Send(s => new RequireAdditionalVerificationCommand(s.OrderId))
                .EndIf()
            .EndIf();

        // 8. Shipping method selection with business rules
        flow.Switch(s => DetermineShippingMethod(s))
            .Case(ShippingMethod.Express, express =>
            {
                express.Send(s => new ScheduleExpressShippingCommand(s.OrderId))
                       .Into(s => s.EstimatedDelivery);
            })
            .Case(ShippingMethod.Standard, standard =>
            {
                standard.Send(s => new ScheduleStandardShippingCommand(s.OrderId))
                        .Into(s => s.EstimatedDelivery);
            })
            .Case(ShippingMethod.Economy, economy =>
            {
                economy.Send(s => new ScheduleEconomyShippingCommand(s.OrderId))
                       .Into(s => s.EstimatedDelivery);
            })
            .EndSwitch();

        // 9. Event publishing for downstream systems
        flow.Publish(s => new OrderProcessedEvent
        {
            OrderId = s.OrderId,
            CustomerId = s.Order.CustomerId,
            TotalAmount = s.Order.TotalAmount,
            ProcessedAt = DateTime.UtcNow,
            InvoiceNumber = s.InvoiceNumber,
            TransactionId = s.TransactionId,
            EstimatedDelivery = s.EstimatedDelivery
        });

        // 10. Final status update with compensation support
        flow.Send(s => new UpdateOrderStatusCommand(s.OrderId, OrderStatus.Confirmed))
            .IfFail(s => new RevertOrderStatusCommand(s.OrderId, OrderStatus.Failed));

        // 11. Metrics and monitoring
        flow.Send(s => new RecordOrderMetricsCommand
        {
            OrderId = s.OrderId,
            ProcessingTime = DateTime.UtcNow - s.StartedAt,
            ItemsProcessed = s.ProcessedItems.Count,
            ItemsFailed = s.FailedItems.Count,
            FraudScore = s.FraudScore,
            PaymentProvider = s.PaymentResult?.Provider ?? string.Empty
        });
    }

    private static ShippingMethod DetermineShippingMethod(OrderFlowState state)
    {
        if (state.Order.CustomerType == CustomerType.VIP)
            return ShippingMethod.Express;

        if (state.Order.TotalAmount > 5000)
            return ShippingMethod.Express;

        if (state.Order.Items.Count > 10)
            return ShippingMethod.Standard;

        return ShippingMethod.Economy;
    }
}

[FlowState]
public partial class OrderFlowState : IFlowState
{
    public string? FlowId { get; set; }

    [FlowStateField]
    private string _orderId = string.Empty;

    [FlowStateField]
    private Order _order = new();

    [FlowStateField]
    private DateTime _startedAt = DateTime.UtcNow;

    [FlowStateField]
    private bool _requiresApproval;

    [FlowStateField]
    private bool _requiresReview;

    [FlowStateField]
    private bool _isApproved;

    [FlowStateField]
    private List<string> _outOfStockItems = new();

    [FlowStateField]
    private Dictionary<string, int> _availableQuantities = new();

    [FlowStateField]
    private Dictionary<string, string> _reservedItems = new();

    [FlowStateField]
    private HashSet<string> _processedItems = new();

    [FlowStateField]
    private List<string> _failedItems = new();

    [FlowStateField]
    private PaymentResult? _paymentResult;

    [FlowStateField]
    private bool _paymentProcessed;

    [FlowStateField]
    private string _paymentProvider = string.Empty;

    [FlowStateField]
    private string _transactionId = string.Empty;

    [FlowStateField]
    private string _invoiceNumber = string.Empty;

    [FlowStateField]
    private int _loyaltyPointsAwarded;

    [FlowStateField]
    private bool _emailSent;

    [FlowStateField]
    private string _shippingLabel = string.Empty;

    [FlowStateField]
    private DateTime _estimatedDelivery;

    [FlowStateField]
    private List<Warehouse> _warehouses = new();

    [FlowStateField]
    private Dictionary<string, int> _warehouseCapacities = new();

    [FlowStateField]
    private List<WarehouseAllocation> _warehouseAllocations = new();

    [FlowStateField]
    private double _fraudScore;

    [FlowStateField]
    private decimal _processingProgress;

    [FlowStateField]
    private List<string> _errors = new();
}

// Supporting models
public class Order
{
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public CustomerType CustomerType { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public ShippingPriority ShippingPriority { get; set; }
}

public class OrderItem
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class Warehouse
{
    public string Id { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public HashSet<string> Products { get; set; } = new();

    public bool HasProduct(string productId) => Products.Contains(productId);
}

public class WarehouseAllocation
{
    public string WarehouseId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public enum CustomerType
{
    New,
    Regular,
    VIP
}

public enum ShippingPriority
{
    Standard,
    Express,
    Overnight
}

public enum ShippingMethod
{
    Economy,
    Standard,
    Express
}

// OrderStatus comes from OrderSystem.Api.Domain
