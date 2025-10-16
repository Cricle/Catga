using Catga.Results;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Services;

public interface IInventoryService
{
    ValueTask<CatgaResult> CheckStockAsync(List<OrderItem> items, CancellationToken cancellationToken = default);
    ValueTask<CatgaResult> ReserveStockAsync(string orderId, List<OrderItem> items, CancellationToken cancellationToken = default);
    ValueTask<CatgaResult> ReleaseStockAsync(string orderId, List<OrderItem> items, CancellationToken cancellationToken = default);
}

public interface IPaymentService
{
    ValueTask<CatgaResult> ProcessPaymentAsync(string orderId, decimal amount, string paymentMethod, CancellationToken cancellationToken = default);
}

