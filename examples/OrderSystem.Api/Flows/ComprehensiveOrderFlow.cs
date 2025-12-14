using Catga.Abstractions;
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
        flow.Timeout(TimeSpan.FromMinutes(10));
        flow.Retry(3).ForTags("critical");

        // Step 1: Fraud check
        flow.Send(s => new PerformFraudCheckCommand(s.OrderId))
            .Into((s, r) => s.FraudScore = r)
            .Tag("security");

        // Step 2: Conditional approval based on fraud score
        flow.If(s => s.FraudScore > 0.8)
            .Send(s => new FlagOrderForReviewCommand(s.OrderId))
            .Send(s => new NotifySecurityTeamCommand(s.OrderId, s.FraudScore))
        .ElseIf(s => s.FraudScore > 0.5)
            .Send(s => new RequireAdditionalVerificationCommand(s.OrderId))
        .Else()
            .Send(s => new AutoApproveOrderCommand(s.OrderId))
            .Into((s, r) => s.IsApproved = r)
        .EndIf();

        // Step 3: Process items - check inventory using Configure pattern
        flow.ForEach(s => s.Order.Items)
            .WithParallelism(5)
            .Configure((item, f) =>
            {
                f.Send(s => new CheckInventoryCommand(item.ProductId, item.Quantity))
                    .Into((s, r) =>
                    {
                        if (r.InStock)
                        {
                            s.AvailableQuantities[item.ProductId] = r.AvailableQuantity;
                            s.ProcessedItems.Add(item.ProductId);
                        }
                        else
                        {
                            s.OutOfStockItems.Add(item.ProductId);
                        }
                    });
            })
            .ContinueOnFailure()
            .OnComplete(s => s.ProcessingProgress = 0.3m)
            .EndForEach();

        // Step 4: Reserve inventory for available items
        flow.ForEach(s => s.Order.Items.Where(i => s.AvailableQuantities.ContainsKey(i.ProductId)))
            .Configure((item, f) =>
            {
                f.Send(s => new ReserveInventoryCommand(item.ProductId, item.Quantity))
                    .Into((s, r) => s.ReservedItems[item.ProductId] = r.ReservationId);
            })
            .StopOnFirstFailure()
            .EndForEach();

        // Step 5: Process payment based on customer type
        flow.Switch(s => s.Order.CustomerType)
            .Case(CustomerType.VIP, c => c
                .Send(s => new ProcessPaymentWithStripeCommand(s.OrderId, s.Order.TotalAmount * 0.9m))
                .Into((s, r) =>
                {
                    s.PaymentResult = new PaymentResult { Provider = r.Provider, TransactionId = r.TransactionId };
                    s.PaymentProcessed = true;
                }))
            .Case(CustomerType.Regular, c => c
                .Send(s => new ProcessPaymentWithPayPalCommand(s.OrderId, s.Order.TotalAmount * 0.95m))
                .Into((s, r) =>
                {
                    s.PaymentResult = new PaymentResult { Provider = r.Provider, TransactionId = r.TransactionId };
                    s.PaymentProcessed = true;
                }))
            .Default(c => c
                .Send(s => new ProcessPaymentWithSquareCommand(s.OrderId, s.Order.TotalAmount))
                .Into((s, r) =>
                {
                    s.PaymentResult = new PaymentResult { Provider = r.Provider, TransactionId = r.TransactionId };
                    s.PaymentProcessed = true;
                }))
            .EndSwitch();

        // Step 6: Generate invoice (single step instead of WhenAll with incompatible types)
        flow.Send(s => new GenerateInvoiceCommand(s.OrderId))
            .Into((s, r) => s.InvoiceNumber = r)
            .Tag("notifications");

        // Step 7: Update loyalty points
        flow.Send(s => new UpdateCustomerLoyaltyPointsCommand(s.Order.CustomerId, s.Order.TotalAmount))
            .Into((s, r) => s.LoyaltyPointsAwarded = r);

        // Step 8: Send confirmation email
        flow.Send(s => new SendOrderConfirmationEmailCommand(s.Order.CustomerEmail, s.OrderId))
            .Into((s, r) => s.EmailSent = r);

        // Step 9: Schedule shipping based on priority
        flow.Switch(s => s.Order.ShippingPriority)
            .Case(ShippingPriority.Overnight, c => c
                .Send(s => new ScheduleExpressShippingCommand(s.OrderId))
                .Into((s, r) => s.EstimatedDelivery = r))
            .Case(ShippingPriority.Express, c => c
                .Send(s => new ScheduleStandardShippingCommand(s.OrderId))
                .Into((s, r) => s.EstimatedDelivery = r))
            .Default(c => c
                .Send(s => new ScheduleEconomyShippingCommand(s.OrderId))
                .Into((s, r) => s.EstimatedDelivery = r))
            .EndSwitch();

        // Step 10: Final notification
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
