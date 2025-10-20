using Catga.Core;
using OrderSystem.Api.Domain;
using System.Collections.Concurrent;

namespace OrderSystem.Api.Services;

public class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<string, Order> _orders = new();

    public ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
        => new(_orders.TryGetValue(orderId, out var order) ? order : null);

    public ValueTask SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.OrderId] = order;
        return default;
    }

    public ValueTask UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.OrderId] = order;
        return default;
    }

    public ValueTask DeleteAsync(string orderId, CancellationToken cancellationToken = default)
    {
        _orders.TryRemove(orderId, out _);
        return default;
    }
}

public class MockInventoryService : IInventoryService
{
    public ValueTask<CatgaResult> CheckStockAsync(List<OrderItem> items, CancellationToken cancellationToken = default)
        => new(CatgaResult.Success());

    public ValueTask<CatgaResult> ReserveStockAsync(string orderId, List<OrderItem> items, CancellationToken cancellationToken = default)
        => new(CatgaResult.Success());

    public ValueTask<CatgaResult> ReleaseStockAsync(string orderId, List<OrderItem> items, CancellationToken cancellationToken = default)
        => new(CatgaResult.Success());
}

public class MockPaymentService : IPaymentService
{
    public ValueTask<CatgaResult> ProcessPaymentAsync(string orderId, decimal amount, string paymentMethod, CancellationToken cancellationToken = default)
        => new(CatgaResult.Success());
}
