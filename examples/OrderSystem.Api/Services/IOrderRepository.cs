using Catga.Results;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Services;

/// <summary>
/// Order repository interface
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task<List<Order>> GetByCustomerIdAsync(string customerId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    Task SaveAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
}

/// <summary>
/// Inventory service interface
/// </summary>
public interface IInventoryService
{
    Task<CatgaResult> CheckStockAsync(List<OrderItem> items, CancellationToken cancellationToken = default);
    Task<CatgaResult> ReserveStockAsync(string orderId, List<OrderItem> items, CancellationToken cancellationToken = default);
    Task<CatgaResult> ReleaseStockAsync(string orderId, List<OrderItem> items, CancellationToken cancellationToken = default);
}

/// <summary>
/// Payment service interface
/// </summary>
public interface IPaymentService
{
    Task<CatgaResult> ProcessPaymentAsync(string orderId, decimal amount, string paymentMethod, CancellationToken cancellationToken = default);
}

