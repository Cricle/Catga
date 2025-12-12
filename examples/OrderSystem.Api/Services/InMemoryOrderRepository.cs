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

    private T WithLock<T>(Func<T> action)
    {
        lock (_lock)
        {
            return action();
        }
    }

    private void WithLock(Action action)
    {
        lock (_lock)
        {
            action();
        }
    }

    public ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken ct = default)
    {
        return ValueTask.FromResult(WithLock(() =>
        {
            _orders.TryGetValue(orderId, out var order);
            return order;
        }));
    }

    public ValueTask<List<Order>> GetByCustomerIdAsync(string customerId, CancellationToken ct = default)
    {
        return ValueTask.FromResult(WithLock(() =>
            _orders.Values.Where(o => o.CustomerId == customerId).ToList()
        ));
    }

    public ValueTask SaveAsync(Order order, CancellationToken ct = default)
    {
        WithLock(() => _orders[order.OrderId] = order);
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(Order order, CancellationToken ct = default)
    {
        WithLock(() => _orders[order.OrderId] = order);
        return ValueTask.CompletedTask;
    }
}
