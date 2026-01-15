using Catga.Abstractions;
using OrderSystem.Events;
using OrderSystem.Models;

namespace OrderSystem.Handlers;

public sealed class OrderEventLogger : IEventHandler<OrderCreatedEvent>, IEventHandler<OrderPaidEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent evt, CancellationToken ct = default)
    {
        Console.WriteLine($"[Event] Order {evt.OrderId} created: ${evt.Total}");
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(OrderPaidEvent evt, CancellationToken ct = default)
    {
        Console.WriteLine($"[Event] Order {evt.OrderId} paid via {evt.PaymentMethod}");
        return ValueTask.CompletedTask;
    }
}

// Event handler to append events to history
public sealed class OrderEventHistoryHandler(OrderStore store) : 
    IEventHandler<OrderCreatedEvent>,
    IEventHandler<OrderPaidEvent>,
    IEventHandler<OrderShippedEvent>,
    IEventHandler<OrderCancelledEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent evt, CancellationToken ct = default)
    {
        store.AppendEvent(evt.OrderId, evt);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(OrderPaidEvent evt, CancellationToken ct = default)
    {
        store.AppendEvent(evt.OrderId, evt);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(OrderShippedEvent evt, CancellationToken ct = default)
    {
        store.AppendEvent(evt.OrderId, evt);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(OrderCancelledEvent evt, CancellationToken ct = default)
    {
        store.AppendEvent(evt.OrderId, evt);
        return ValueTask.CompletedTask;
    }
}
