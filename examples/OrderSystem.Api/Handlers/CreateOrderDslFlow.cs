using Catga.Abstractions;
using Catga.Flow.Dsl;
using MemoryPack;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Order creation flow state using DSL Flow.
/// Demonstrates: Distributed saga with automatic compensation.
///
/// Note: For production use, consider using [FlowState] attribute with field declarations
/// to auto-generate change tracking. This example shows manual implementation.
/// </summary>
public class CreateOrderFlowState : IFlowState
{
    public const int Field_OrderId = 0;
    public const int Field_CustomerId = 1;
    public const int Field_Items = 2;
    public const int Field_ShippingAddress = 3;
    public const int Field_PaymentMethod = 4;
    public const int Field_TotalAmount = 5;
    public const int Field_ReservationId = 6;
    public const int Field_PaymentId = 7;
    public const int Field_Status = 8;
    public const int FieldCount = 9;

    private int _changedMask;
    public string? FlowId { get; set; }

    private string? _orderId;
    public string? OrderId { get => _orderId; set { _orderId = value; MarkChanged(Field_OrderId); } }

    private string? _customerId;
    public string? CustomerId { get => _customerId; set { _customerId = value; MarkChanged(Field_CustomerId); } }

    private List<OrderItem>? _items;
    public List<OrderItem>? Items { get => _items; set { _items = value; MarkChanged(Field_Items); } }

    private string? _shippingAddress;
    public string? ShippingAddress { get => _shippingAddress; set { _shippingAddress = value; MarkChanged(Field_ShippingAddress); } }

    private string? _paymentMethod;
    public string? PaymentMethod { get => _paymentMethod; set { _paymentMethod = value; MarkChanged(Field_PaymentMethod); } }

    private decimal _totalAmount;
    public decimal TotalAmount { get => _totalAmount; set { _totalAmount = value; MarkChanged(Field_TotalAmount); } }

    private string? _reservationId;
    public string? ReservationId { get => _reservationId; set { _reservationId = value; MarkChanged(Field_ReservationId); } }

    private string? _paymentId;
    public string? PaymentId { get => _paymentId; set { _paymentId = value; MarkChanged(Field_PaymentId); } }

    private string? _status;
    public string? Status { get => _status; set { _status = value; MarkChanged(Field_Status); } }

    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames()
    {
        if (IsFieldChanged(Field_OrderId)) yield return nameof(OrderId);
        if (IsFieldChanged(Field_CustomerId)) yield return nameof(CustomerId);
        if (IsFieldChanged(Field_Items)) yield return nameof(Items);
        if (IsFieldChanged(Field_ShippingAddress)) yield return nameof(ShippingAddress);
        if (IsFieldChanged(Field_PaymentMethod)) yield return nameof(PaymentMethod);
        if (IsFieldChanged(Field_TotalAmount)) yield return nameof(TotalAmount);
        if (IsFieldChanged(Field_ReservationId)) yield return nameof(ReservationId);
        if (IsFieldChanged(Field_PaymentId)) yield return nameof(PaymentId);
        if (IsFieldChanged(Field_Status)) yield return nameof(Status);
    }
}

/// <summary>
/// Order creation flow configuration using DSL.
///
/// Flow steps:
/// 1. Reserve Inventory → Compensation: Release inventory
/// 2. Process Payment → Compensation: Refund payment
/// 3. Confirm Order (final step)
///
/// On any failure, compensations execute in reverse order automatically.
/// </summary>
public class CreateOrderFlowConfig : FlowConfig<CreateOrderFlowState>
{
    protected override void Configure(IFlowBuilder<CreateOrderFlowState> flow)
    {
        flow.Name("create-order");

        // Global settings
        flow.Timeout(TimeSpan.FromMinutes(5));
        flow.Persist().ForTags("critical");

        // Step 1: Reserve inventory
        flow.Send(s => new ReserveInventoryCommand(s.OrderId!, s.Items!))
            .FailIf((ReserveInventoryResult r) => string.IsNullOrEmpty(r.ReservationId), "Reservation failed")
            .IfFail(s => new ReleaseInventoryCommand(s.ReservationId!))
            .Tag("critical");

        // Step 2: Process payment
        flow.Send(s => new ProcessPaymentCommand(s.OrderId!, s.TotalAmount, s.PaymentMethod!))
            .FailIf((ProcessPaymentResult r) => string.IsNullOrEmpty(r.PaymentId), "Payment failed")
            .IfFail(s => new RefundPaymentCommand(s.PaymentId!))
            .Tag("critical");

        // Step 3: Confirm order
        flow.Send(s => new ConfirmOrderCommand(s.OrderId!));

        // Event hooks
        flow.OnFlowCompleted(s => new OrderFlowCompletedEvent(s.OrderId!, s.PaymentId!));
        flow.OnFlowFailed((s, error) => new OrderFlowFailedEvent(s.OrderId!, error));
    }
}

// Commands for the flow
[MemoryPackable]
public partial record ConfirmOrderCommand(string OrderId) : IRequest
{
    public long MessageId => 0;
}

// Events for the flow
[MemoryPackable]
public partial record OrderFlowCompletedEvent(string OrderId, string PaymentId) : IEvent
{
    public long MessageId => 0;
}

[MemoryPackable]
public partial record OrderFlowFailedEvent(string OrderId, string? Error) : IEvent
{
    public long MessageId => 0;
}

/// <summary>
/// Handler for CreateOrderDslFlowCommand using the new DSL Flow.
/// </summary>
public class CreateOrderDslFlowHandler(IFlow<CreateOrderFlowState> flow)
{
    public async Task<OrderCreatedResult> HandleAsync(CreateOrderDslFlowCommand command, CancellationToken ct)
    {
        var state = new CreateOrderFlowState
        {
            OrderId = Guid.NewGuid().ToString("N"),
            CustomerId = command.CustomerId,
            Items = command.Items,
            ShippingAddress = command.ShippingAddress,
            PaymentMethod = command.PaymentMethod,
            TotalAmount = command.Items.Sum(i => i.UnitPrice * i.Quantity),
            Status = "Pending"
        };

        var result = await flow.RunAsync(state, ct);

        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Error ?? "Order creation failed");

        return new OrderCreatedResult(
            state.OrderId,
            state.TotalAmount,
            DateTime.UtcNow);
    }
}

/// <summary>
/// Command to create order using DSL Flow.
/// </summary>
[MemoryPackable]
public partial record CreateOrderDslFlowCommand(
    string CustomerId,
    List<OrderItem> Items,
    string ShippingAddress,
    string PaymentMethod
) : IRequest<OrderCreatedResult>
{
    public long MessageId => 0;
}
