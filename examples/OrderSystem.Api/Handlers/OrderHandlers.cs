using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

public class CreateOrderHandler(
    IOrderRepository orderRepository,
    ILogger<CreateOrderHandler> logger) : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand request, CancellationToken ct = default)
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

public class CancelOrderHandler(
    IOrderRepository orderRepository,
    ILogger<CancelOrderHandler> logger) : IRequestHandler<CancelOrderCommand>
{
    public async Task<CatgaResult> HandleAsync(
        CancelOrderCommand request, CancellationToken ct = default)
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

public class GetOrderHandler(
    IOrderRepository orderRepository) : IRequestHandler<GetOrderQuery, Order?>
{
    public async Task<CatgaResult<Order?>> HandleAsync(
        GetOrderQuery request, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        return CatgaResult<Order?>.Success(order);
    }
}

public class GetUserOrdersHandler(
    IOrderRepository orderRepository) : IRequestHandler<GetUserOrdersQuery, List<Order>>
{
    public async Task<CatgaResult<List<Order>>> HandleAsync(
        GetUserOrdersQuery request, CancellationToken ct = default)
    {
        var orders = await orderRepository.GetByCustomerIdAsync(request.CustomerId, ct);
        return CatgaResult<List<Order>>.Success(orders);
    }
}
