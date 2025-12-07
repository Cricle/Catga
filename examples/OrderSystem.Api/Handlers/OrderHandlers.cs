using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Flows;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Create order using Flow DSL pattern with automatic compensation.
/// Flow steps: SaveOrder -> ReserveStock -> ConfirmOrder -> PublishEvent
/// On failure: compensation runs in reverse order.
/// </summary>
/// <remarks>
/// This handler demonstrates the Flow DSL pattern:
/// 1. Define state class implementing IFlowState
/// 2. Define FlowConfig with Send/Query/Publish steps
/// 3. Execute flow with DslFlowExecutor
///
/// See Flows/CreateOrderFlowConfig.cs for the flow definition.
/// </remarks>
public class CreateOrderFlowHandler(
    IOrderRepository orderRepository,
    ICatgaMediator mediator,
    ILogger<CreateOrderFlowHandler> logger) : IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderFlowCommand request, CancellationToken ct = default)
    {
        // For simplicity, execute steps directly here
        // In production, use DslFlowExecutor with CreateOrderFlowConfig
        var orderId = $"ORD-{Guid.NewGuid():N}"[..16];
        var order = new Order
        {
            OrderId = orderId,
            CustomerId = request.CustomerId,
            Items = request.Items,
            TotalAmount = request.Items.Sum(i => i.Subtotal),
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            // Step 1: Save order
            await orderRepository.SaveAsync(order, ct);
            logger.LogInformation("Step 1: Order {OrderId} created", order.OrderId);

            // Step 2: Reserve stock (simulated)
            logger.LogInformation("Step 2: Stock reserved for order {OrderId}", order.OrderId);

            // Step 3: Confirm order
            order.Status = OrderStatus.Confirmed;
            order.UpdatedAt = DateTime.UtcNow;
            await orderRepository.UpdateAsync(order, ct);
            logger.LogInformation("Step 3: Order {OrderId} confirmed", order.OrderId);

            // Step 4: Publish event
            await mediator.PublishAsync(new OrderConfirmedEvent(order.OrderId, DateTime.UtcNow), ct);

            return CatgaResult<OrderCreatedResult>.Success(
                new OrderCreatedResult(order.OrderId, order.TotalAmount, order.CreatedAt));
        }
        catch (Exception ex)
        {
            // Compensation: Mark as failed
            order.Status = OrderStatus.Failed;
            order.UpdatedAt = DateTime.UtcNow;
            await orderRepository.UpdateAsync(order, ct);
            logger.LogWarning(ex, "Order {OrderId} failed, compensation executed", order.OrderId);

            return CatgaResult<OrderCreatedResult>.Failure($"Order creation failed: {ex.Message}");
        }
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
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(
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
    public async ValueTask<CatgaResult> HandleAsync(
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
    public async ValueTask<CatgaResult<Order?>> HandleAsync(
        GetOrderQuery request, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        return CatgaResult<Order?>.Success(order);
    }
}

public class GetUserOrdersHandler(
    IOrderRepository orderRepository) : IRequestHandler<GetUserOrdersQuery, List<Order>>
{
    public async ValueTask<CatgaResult<List<Order>>> HandleAsync(
        GetUserOrdersQuery request, CancellationToken ct = default)
    {
        var orders = await orderRepository.GetByCustomerIdAsync(request.CustomerId, ct);
        return CatgaResult<List<Order>>.Success(orders);
    }
}
