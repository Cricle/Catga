using Catga.Abstractions;
using Catga.Flow.Dsl;
using MemoryPack;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Order creation flow state using DSL Flow with Source Generator.
///
/// The [FlowState] attribute auto-generates:
/// - IFlowState implementation
/// - Change tracking (HasChanges, GetChangedMask, etc.)
/// - Field constants (Field_OrderId, Field_CustomerId, etc.)
/// - FlowId property
///
/// Just define properties - the generator handles the rest!
/// </summary>
[FlowState]
public partial class CreateOrderFlowState
{
    public string? OrderId { get; set; }
    public string? CustomerId { get; set; }
    public List<OrderItem>? Items { get; set; }
    public string? ShippingAddress { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal TotalAmount { get; set; }
    public string? ReservationId { get; set; }
    public string? PaymentId { get; set; }
    public string? Status { get; set; }
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
