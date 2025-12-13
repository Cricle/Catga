using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Services;

public interface IOrderRepository
{
    ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken ct = default);
    ValueTask<List<Order>> GetByCustomerIdAsync(string customerId, CancellationToken ct = default);
    ValueTask<List<Order>> GetAllAsync(OrderStatus? status = null, int limit = 100, CancellationToken ct = default);
    ValueTask<OrderStats> GetStatsAsync(CancellationToken ct = default);
    ValueTask SaveAsync(Order order, CancellationToken ct = default);
    ValueTask UpdateAsync(Order order, CancellationToken ct = default);
    ValueTask DeleteAsync(string orderId, CancellationToken ct = default);
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
            var orders = _orders.Values
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.CreatedAt)
                .ToList();
            return ValueTask.FromResult(orders);
        }
    }

    public ValueTask<List<Order>> GetAllAsync(OrderStatus? status = null, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var query = _orders.Values.AsEnumerable();
            if (status.HasValue)
                query = query.Where(o => o.Status == status.Value);

            var orders = query
                .OrderByDescending(o => o.CreatedAt)
                .Take(limit)
                .ToList();
            return ValueTask.FromResult(orders);
        }
    }

    public ValueTask<OrderStats> GetStatsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var orders = _orders.Values;
            var stats = new OrderStats(
                Total: orders.Count,
                Pending: orders.Count(o => o.Status == OrderStatus.Pending),
                Paid: orders.Count(o => o.Status == OrderStatus.Paid),
                Processing: orders.Count(o => o.Status == OrderStatus.Processing),
                Shipped: orders.Count(o => o.Status == OrderStatus.Shipped),
                Delivered: orders.Count(o => o.Status == OrderStatus.Delivered),
                Cancelled: orders.Count(o => o.Status == OrderStatus.Cancelled),
                TotalRevenue: orders.Where(o => o.Status == OrderStatus.Delivered).Sum(o => o.TotalAmount)
            );
            return ValueTask.FromResult(stats);
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

    public ValueTask DeleteAsync(string orderId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _orders.Remove(orderId);
            return ValueTask.CompletedTask;
        }
    }
}
