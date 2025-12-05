using Catga;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Create order - POST /api/orders
/// </summary>
[CatgaHandler]
[Route("/orders")]
public sealed partial class CreateOrderHandler(
    IOrderRepository orderRepository,
    ILogger<CreateOrderHandler> logger) : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private async Task<CatgaResult<OrderCreatedResult>> HandleAsyncCore(
        CreateOrderCommand request, CancellationToken ct)
    {
        var order = new Order
        {
            OrderId = $"ORD-{Guid.NewGuid():N}"[..16],
            CustomerId = request.CustomerId,
            Items = request.Items,
            TotalAmount = request.Items.Sum(i => i.Subtotal),
            Status = OrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow
        };

        await orderRepository.SaveAsync(order, ct);
        logger.LogInformation("Order {OrderId} created", order.OrderId);

        return CatgaResult<OrderCreatedResult>.Success(
            new OrderCreatedResult(order.OrderId, order.TotalAmount, order.CreatedAt));
    }
}

/// <summary>
/// Cancel order - POST /api/orders/{orderId}/cancel
/// </summary>
[CatgaHandler]
[Route("/orders/{orderId}/cancel")]
public sealed partial class CancelOrderHandler(
    IOrderRepository orderRepository,
    ILogger<CancelOrderHandler> logger) : IRequestHandler<CancelOrderCommand>
{
    private async Task<CatgaResult> HandleAsyncCore(
        CancelOrderCommand request, CancellationToken ct)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        if (order == null)
            return CatgaResult.Failure($"Order {request.OrderId} not found");

        if (order.Status == OrderStatus.Cancelled)
            return CatgaResult.Success();

        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = request.Reason;
        order.CancelledAt = DateTime.UtcNow;

        await orderRepository.UpdateAsync(order, ct);
        logger.LogInformation("Order {OrderId} cancelled", request.OrderId);

        return CatgaResult.Success();
    }
}

/// <summary>
/// Get order - GET /api/orders/{orderId}
/// </summary>
[CatgaHandler]
[Route("/orders/{orderId}", Method = "GET")]
public sealed partial class GetOrderHandler(
    IOrderRepository orderRepository) : IRequestHandler<GetOrderQuery, Order?>
{
    private async Task<CatgaResult<Order?>> HandleAsyncCore(
        GetOrderQuery request, CancellationToken ct)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        return CatgaResult<Order?>.Success(order);
    }
}

/// <summary>
/// Get user orders - GET /api/users/{customerId}/orders
/// </summary>
[CatgaHandler]
[Route("/users/{customerId}/orders", Method = "GET")]
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
