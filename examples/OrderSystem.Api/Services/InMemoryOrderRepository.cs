using System.Collections.Concurrent;
using Catga;
using Catga.Results;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Services;

/// <summary>
/// In-memory order repository (demo implementation)
/// </summary>
[CatgaService(Catga.ServiceLifetime.Singleton, ServiceType = typeof(IOrderRepository))]
public class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<string, Order> _orders = new();
    private readonly ILogger<InMemoryOrderRepository> _logger;

    public InMemoryOrderRepository(ILogger<InMemoryOrderRepository> logger)
    {
        _logger = logger;
    }

    public Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        _orders.TryGetValue(orderId, out var order);
        return Task.FromResult(order);
    }

    public Task<List<Order>> GetByCustomerIdAsync(
        string customerId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var orders = _orders.Values
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(orders);
    }

    public Task SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.OrderId] = order;
        _logger.LogDebug("Order saved: {OrderId}", order.OrderId);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.OrderId] = order;
        _logger.LogDebug("Order updated: {OrderId}, status: {Status}", order.OrderId, order.Status);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string orderId, CancellationToken cancellationToken = default)
    {
        _orders.TryRemove(orderId, out _);
        _logger.LogDebug("Order deleted: {OrderId}", orderId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Mock inventory service (demo implementation)
/// </summary>
[CatgaService(Catga.ServiceLifetime.Singleton, ServiceType = typeof(IInventoryService))]
public class MockInventoryService : IInventoryService
{
    private readonly ILogger<MockInventoryService> _logger;

    public MockInventoryService(ILogger<MockInventoryService> logger)
    {
        _logger = logger;
    }

    public Task<CatgaResult> CheckStockAsync(List<OrderItem> items, CancellationToken cancellationToken = default)
    {
        // Mock inventory check
        _logger.LogInformation("Stock checked: {Count} items", items.Count);
        return Task.FromResult(CatgaResult.Success());
    }

    public Task<CatgaResult> ReserveStockAsync(string orderId, List<OrderItem> items, CancellationToken cancellationToken = default)
    {
        // Mock inventory reservation
        _logger.LogInformation("Stock reserved: order {OrderId}, {Count} items", orderId, items.Count);
        return Task.FromResult(CatgaResult.Success());
    }

    public Task<CatgaResult> ReleaseStockAsync(string orderId, List<OrderItem> items, CancellationToken cancellationToken = default)
    {
        // Mock inventory release
        _logger.LogInformation("Stock released: order {OrderId}, {Count} items", orderId, items.Count);
        return Task.FromResult(CatgaResult.Success());
    }
}

/// <summary>
/// Mock payment service (demo implementation)
/// </summary>
[CatgaService(Catga.ServiceLifetime.Singleton, ServiceType = typeof(IPaymentService))]
public class MockPaymentService : IPaymentService
{
    private readonly ILogger<MockPaymentService> _logger;

    public MockPaymentService(ILogger<MockPaymentService> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult> ProcessPaymentAsync(
        string orderId,
        decimal amount,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        // Mock payment processing
        _logger.LogInformation("Payment processed: order {OrderId}, amount {Amount}, method {Method}",
            orderId, amount, paymentMethod);

        // Simulate network delay
        await Task.Delay(100, cancellationToken);

        return CatgaResult.Success();
    }
}
