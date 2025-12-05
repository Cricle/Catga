using Catga.Flow.Dsl;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Flows;

/// <summary>
/// Flow state for order creation.
/// Uses [FlowState] attribute for source-generated IFlowState implementation.
/// </summary>
[FlowState]
public partial class CreateOrderFlowState
{
    public string? OrderId { get; set; }
    public decimal TotalAmount { get; set; }
    public bool StockReserved { get; set; }

    [FlowStateIgnore]
    public string? CustomerId { get; set; }

    [FlowStateIgnore]
    public List<OrderSystem.Api.Domain.OrderItem> Items { get; set; } = [];
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
