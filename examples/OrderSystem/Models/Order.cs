using MemoryPack;

namespace OrderSystem.Models;

public enum OrderStatus { Pending, Paid, Shipped, Delivered, Cancelled }

[MemoryPackable]
public partial record OrderItem(string ProductId, string Name, int Quantity, decimal Price);

[MemoryPackable]
public partial record Order(
    string Id,
    string CustomerId,
    List<OrderItem> Items,
    OrderStatus Status,
    decimal Total,
    DateTime CreatedAt,
    DateTime? PaidAt = null,
    DateTime? ShippedAt = null,
    string? TrackingNumber = null);

public sealed class OrderStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Order> _orders = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<object>> _events = new();

    public void Save(Order order) => _orders[order.Id] = order;

    public Order? Get(string id) => _orders.GetValueOrDefault(id);

    public List<Order> GetAll() => [.. _orders.Values];

    public void AppendEvent(string orderId, object evt)
    {
        var events = _events.GetOrAdd(orderId, _ => []);
        lock (events) events.Add(evt);
    }

    public List<object> GetEvents(string orderId) =>
        _events.TryGetValue(orderId, out var events) ? [.. events] : [];
}
