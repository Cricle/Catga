using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Services;

public interface IOrderRepository
{
    ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken ct = default);
    ValueTask<List<Order>> GetByCustomerIdAsync(string customerId, CancellationToken ct = default);
    ValueTask SaveAsync(Order order, CancellationToken ct = default);
    ValueTask UpdateAsync(Order order, CancellationToken ct = default);
}

public class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<string, Order> _orders = new();
    private readonly Lock _lock = new();

    public ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _orders.TryGetValue(orderId, out var order);
            return ValueTask.FromResult(order);
        }
    }

    public ValueTask<List<Order>> GetByCustomerIdAsync(string customerId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var orders = _orders.Values.Where(o => o.CustomerId == customerId).ToList();
            return ValueTask.FromResult(orders);
        }
    }

    public ValueTask SaveAsync(Order order, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _orders[order.OrderId] = order;
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask UpdateAsync(Order order, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _orders[order.OrderId] = order;
            return ValueTask.CompletedTask;
        }
    }
}
