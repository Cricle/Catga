using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Create order using Flow pattern with automatic compensation.
/// Flow steps: CreateOrder -> ReserveStock -> ConfirmOrder
/// On failure: compensation runs in reverse order.
/// </summary>
public partial class CreateOrderFlowHandler(
    IOrderRepository orderRepository,
    ICatgaMediator mediator,
    ILogger<CreateOrderFlowHandler> logger) : IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>
{
    private Order _order = null!;

    [FlowStep(Order = 1)]
    private async Task CreateOrder(CreateOrderFlowCommand request, CancellationToken ct)
    {
        _order = new Order
        {
            OrderId = $"ORD-{Guid.NewGuid():N}"[..16],
            CustomerId = request.CustomerId,
            Items = request.Items,
            TotalAmount = request.Items.Sum(i => i.Subtotal),
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepository.SaveAsync(_order, ct);
        logger.LogInformation("Step 1: Order {OrderId} created", _order.OrderId);
    }

    [FlowStep(Order = 2, Compensate = nameof(ReleaseStock))]
    private Task ReserveStock(CreateOrderFlowCommand request, CancellationToken ct)
    {
        // Simulate stock reservation (in real app, call inventory service)
        logger.LogInformation("Step 2: Stock reserved for order {OrderId}", _order.OrderId);
        return Task.CompletedTask;
    }

    private Task ReleaseStock(CreateOrderFlowCommand request, CancellationToken ct)
    {
        logger.LogWarning("Compensation: Releasing stock for order {OrderId}", _order.OrderId);
        return Task.CompletedTask;
    }

    [FlowStep(Order = 3, Compensate = nameof(MarkFailed))]
    private async Task ConfirmOrder(CreateOrderFlowCommand request, CancellationToken ct)
    {
        _order.Status = OrderStatus.Confirmed;
        _order.UpdatedAt = DateTime.UtcNow;
        await orderRepository.UpdateAsync(_order, ct);
        logger.LogInformation("Step 3: Order {OrderId} confirmed", _order.OrderId);

        // Publish event
        await mediator.PublishAsync(new OrderConfirmedEvent(_order.OrderId, DateTime.UtcNow), ct);
    }

    private async Task MarkFailed(CreateOrderFlowCommand request, CancellationToken ct)
    {
        _order.Status = OrderStatus.Failed;
        _order.UpdatedAt = DateTime.UtcNow;
        await orderRepository.UpdateAsync(_order, ct);
        logger.LogWarning("Compensation: Order {OrderId} marked as failed", _order.OrderId);
    }

    public async Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderFlowCommand request, CancellationToken ct = default)
    {
        var result = await ExecuteFlowAsync(request, ct);

        if (!result.IsSuccess)
            return CatgaResult<OrderCreatedResult>.Failure(result.Error ?? "Order creation failed");

        return CatgaResult<OrderCreatedResult>.Success(
            new OrderCreatedResult(_order.OrderId, _order.TotalAmount, _order.CreatedAt));
    }
}

/// <summary>
/// Simple create order handler (without Flow).
/// </summary>
public class CreateOrderHandler(
    IOrderRepository orderRepository,
    ICatgaMediator mediator,
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

        // Publish event for other handlers
        await mediator.PublishAsync(
            new OrderCreatedEvent(order.OrderId, order.CustomerId, order.TotalAmount, order.CreatedAt), ct);

        return CatgaResult<OrderCreatedResult>.Success(
            new OrderCreatedResult(order.OrderId, order.TotalAmount, order.CreatedAt));
    }
}

public class CancelOrderHandler(
    IOrderRepository orderRepository,
    ICatgaMediator mediator,
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

        // Publish cancellation event
        await mediator.PublishAsync(
            new OrderCancelledEvent(request.OrderId, request.Reason, order.CancelledAt!.Value), ct);

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
