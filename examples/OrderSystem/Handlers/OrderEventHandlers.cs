using Catga.Abstractions;
using OrderSystem.Events;

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
