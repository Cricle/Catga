using Catga.Flow.Dsl;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Flows;

/// <summary>
/// Flow state for order creation.
/// Implements IFlowState for change tracking.
/// </summary>
public class CreateOrderFlowState : IFlowState
{
    private int _changedMask;
    private const int Field_OrderId = 0;
    private const int Field_TotalAmount = 1;
    private const int Field_StockReserved = 2;

    public string? FlowId { get; set; }

    private string? _orderId;
    public string? OrderId
    {
        get => _orderId;
        set { _orderId = value; MarkChanged(Field_OrderId); }
    }

    private decimal _totalAmount;
    public decimal TotalAmount
    {
        get => _totalAmount;
        set { _totalAmount = value; MarkChanged(Field_TotalAmount); }
    }

    private bool _stockReserved;
    public bool StockReserved
    {
        get => _stockReserved;
        set { _stockReserved = value; MarkChanged(Field_StockReserved); }
    }

    public string? CustomerId { get; set; }
    public List<OrderSystem.Api.Domain.OrderItem> Items { get; set; } = [];

    // IFlowState implementation
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames()
    {
        if (IsFieldChanged(Field_OrderId)) yield return nameof(OrderId);
        if (IsFieldChanged(Field_TotalAmount)) yield return nameof(TotalAmount);
        if (IsFieldChanged(Field_StockReserved)) yield return nameof(StockReserved);
    }
}

/// <summary>
/// Flow configuration for order creation using DSL.
/// Steps: SaveOrder -> ReserveStock -> ConfirmOrder -> PublishEvent
/// </summary>
public class CreateOrderFlowConfig : FlowConfig<CreateOrderFlowState>
{
    protected override void Configure(IFlowBuilder<CreateOrderFlowState> flow)
    {
        flow.Name("create-order");

        // Global settings
        flow.Timeout(TimeSpan.FromMinutes(5));
        flow.Retry(3).ForTags("critical");

        // Step 1: Save order (with compensation to delete)
        flow.Send(s => new SaveOrderFlowCommand(s.OrderId!, s.CustomerId!, s.Items, s.TotalAmount))
            .IfFail(s => new DeleteOrderFlowCommand(s.OrderId!))
            .Tag("persistence");

        // Step 2: Reserve stock (with compensation to release)
        flow.Send(s => new ReserveStockCommand(s.OrderId!, s.Items))
            .IfFail(s => new ReleaseStockCommand(s.OrderId!))
            .Tag("inventory", "critical");

        // Step 3: Confirm order (with compensation to mark failed)
        flow.Send(s => new ConfirmOrderFlowCommand(s.OrderId!))
            .IfFail(s => new MarkOrderFailedCommand(s.OrderId!))
            .Tag("persistence");

        // Step 4: Publish order confirmed event
        flow.Publish(s => new OrderConfirmedEvent(s.OrderId!, DateTime.UtcNow))
            .Tag("notification");
    }
}

// Flow-specific commands (internal to flow)
public record SaveOrderFlowCommand(
    string OrderId,
    string CustomerId,
    List<OrderSystem.Api.Domain.OrderItem> Items,
    decimal TotalAmount) : Catga.Abstractions.IRequest
{
    public long MessageId => 0;
}

public record DeleteOrderFlowCommand(string OrderId) : Catga.Abstractions.IRequest
{
    public long MessageId => 0;
}

public record ReserveStockCommand(
    string OrderId,
    List<OrderSystem.Api.Domain.OrderItem> Items) : Catga.Abstractions.IRequest
{
    public long MessageId => 0;
}

public record ReleaseStockCommand(string OrderId) : Catga.Abstractions.IRequest
{
    public long MessageId => 0;
}

public record ConfirmOrderFlowCommand(string OrderId) : Catga.Abstractions.IRequest
{
    public long MessageId => 0;
}

public record MarkOrderFailedCommand(string OrderId) : Catga.Abstractions.IRequest
{
    public long MessageId => 0;
}
