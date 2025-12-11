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
        // Simplified flow - focus on FlowState source generation
    }

    // Flow configuration methods removed - focus on FlowState source generation
    // These can be implemented later with proper command type matching

    // Flow configuration methods disabled - focus on FlowState source generation
    // These can be implemented later with proper command type matching
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
