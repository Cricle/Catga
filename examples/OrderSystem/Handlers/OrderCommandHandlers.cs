using Catga;
using Catga.Abstractions;
using Catga.Core;
using OrderSystem.Commands;
using OrderSystem.Events;
using OrderSystem.Models;

namespace OrderSystem.Handlers;

public sealed class CreateOrderHandler(OrderStore store, ICatgaMediator mediator) : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        var orderId = Guid.NewGuid().ToString("N")[..8];
        var total = cmd.Items.Sum(i => i.Price * i.Quantity);
        var now = DateTime.UtcNow;
        store.Save(new Order(orderId, cmd.CustomerId, cmd.Items, OrderStatus.Pending, total, now));
        store.AppendEvent(orderId, new OrderCreatedEvent(orderId, cmd.CustomerId, total, now));
        await mediator.PublishAsync(new OrderCreatedEvent(orderId, cmd.CustomerId, total, now), ct);
        return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(orderId, cmd.CustomerId, total, now));
    }
}

public sealed class PayOrderHandler(OrderStore store, ICatgaMediator mediator) : IRequestHandler<PayOrderCommand>
{
    public async ValueTask<CatgaResult> HandleAsync(PayOrderCommand cmd, CancellationToken ct = default)
    {
        var order = store.Get(cmd.OrderId);
        if (order == null) return CatgaResult.Failure("Order not found");
        if (order.Status != OrderStatus.Pending) return CatgaResult.Failure("Order cannot be paid");
        var now = DateTime.UtcNow;
        store.Save(order with { Status = OrderStatus.Paid, PaidAt = now });
        store.AppendEvent(cmd.OrderId, new OrderPaidEvent(cmd.OrderId, cmd.PaymentMethod, now));
        await mediator.PublishAsync(new OrderPaidEvent(cmd.OrderId, cmd.PaymentMethod, now), ct);
        return CatgaResult.Success();
    }
}

public sealed class ShipOrderHandler(OrderStore store, ICatgaMediator mediator) : IRequestHandler<ShipOrderCommand>
{
    public async ValueTask<CatgaResult> HandleAsync(ShipOrderCommand cmd, CancellationToken ct = default)
    {
        var order = store.Get(cmd.OrderId);
        if (order == null) return CatgaResult.Failure("Order not found");
        if (order.Status != OrderStatus.Paid) return CatgaResult.Failure("Order must be paid first");
        var now = DateTime.UtcNow;
        store.Save(order with { Status = OrderStatus.Shipped, ShippedAt = now, TrackingNumber = cmd.TrackingNumber });
        store.AppendEvent(cmd.OrderId, new OrderShippedEvent(cmd.OrderId, cmd.TrackingNumber, now));
        await mediator.PublishAsync(new OrderShippedEvent(cmd.OrderId, cmd.TrackingNumber, now), ct);
        return CatgaResult.Success();
    }
}

public sealed class CancelOrderHandler(OrderStore store, ICatgaMediator mediator) : IRequestHandler<CancelOrderCommand>
{
    public async ValueTask<CatgaResult> HandleAsync(CancelOrderCommand cmd, CancellationToken ct = default)
    {
        var order = store.Get(cmd.OrderId);
        if (order == null) return CatgaResult.Failure("Order not found");
        if (order.Status is OrderStatus.Shipped or OrderStatus.Delivered) return CatgaResult.Failure("Cannot cancel shipped order");
        var now = DateTime.UtcNow;
        store.Save(order with { Status = OrderStatus.Cancelled });
        store.AppendEvent(cmd.OrderId, new OrderCancelledEvent(cmd.OrderId, now));
        await mediator.PublishAsync(new OrderCancelledEvent(cmd.OrderId, now), ct);
        return CatgaResult.Success();
    }
}
