using Catga;
using Catga.Abstractions;
using Catga.Core;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Query to get all orders for a customer.
/// </summary>
public record GetUserOrdersQuery(string CustomerId) : IRequest<List<Order>>
{
    public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
}

/// <summary>
/// Get user orders - sharded by customer ID for horizontal scaling.
///
/// Framework auto-generates:
/// - Route to correct shard based on CustomerId
/// - Telemetry and metrics
/// - Endpoint: GET /api/users/{customerId}/orders
/// </summary>
[CatgaHandler]
[Route("/users/{customerId}/orders", Method = "GET")]
[Sharded("{request.CustomerId}")]
public sealed partial class GetUserOrdersHandler(
    IOrderRepository orderRepository) : IRequestHandler<GetUserOrdersQuery, List<Order>>
{
    private async Task<CatgaResult<List<Order>>> HandleAsyncCore(
        GetUserOrdersQuery request, CancellationToken ct)
    {
        var orders = await orderRepository.GetByCustomerIdAsync(request.CustomerId, ct);
        return CatgaResult<List<Order>>.Success(orders);
    }
}
