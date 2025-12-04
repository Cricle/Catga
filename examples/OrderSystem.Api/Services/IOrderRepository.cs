using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Services;

public interface IOrderRepository
{
    ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default);
    ValueTask<List<Order>> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(Order order, CancellationToken cancellationToken = default);
    ValueTask UpdateAsync(Order order, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(string orderId, CancellationToken cancellationToken = default);
}
