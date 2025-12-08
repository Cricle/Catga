using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

// ============================================
// Command Handlers
// ============================================

/// <summary>Simple create order handler.</summary>
public class CreateOrderHandler(
    IOrderRepository orderRepository,
    ICatgaMediator mediator,
    ILogger<CreateOrderHandler> logger) : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(CreateOrderCommand request, CancellationToken ct = default)
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
        await mediator.PublishAsync(new OrderCreatedEvent(order.OrderId, order.CustomerId, order.TotalAmount, order.CreatedAt), ct);

        return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(order.OrderId, order.TotalAmount, order.CreatedAt));
    }
}

/// <summary>Create order using Flow pattern with automatic compensation.</summary>
public class CreateOrderFlowHandler(
    IOrderRepository orderRepository,
    ICatgaMediator mediator,
    ILogger<CreateOrderFlowHandler> logger) : IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>
{
    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(CreateOrderFlowCommand request, CancellationToken ct = default)
    {
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
            logger.LogInformation("Step 1: Order {OrderId} saved", order.OrderId);

            // Step 2: Reserve stock (simulated)
            logger.LogInformation("Step 2: Stock reserved for {OrderId}", order.OrderId);

            // Step 3: Confirm order
            order.Status = OrderStatus.Confirmed;
            order.UpdatedAt = DateTime.UtcNow;
            await orderRepository.UpdateAsync(order, ct);
            logger.LogInformation("Step 3: Order {OrderId} confirmed", order.OrderId);

            // Step 4: Publish event
            await mediator.PublishAsync(new OrderConfirmedEvent(order.OrderId, DateTime.UtcNow), ct);

            return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(order.OrderId, order.TotalAmount, order.CreatedAt));
        }
        catch (Exception ex)
        {
            // Compensation: Mark as failed
            order.Status = OrderStatus.Failed;
            await orderRepository.UpdateAsync(order, ct);
            logger.LogWarning(ex, "Order {OrderId} failed, compensation executed", order.OrderId);
            return CatgaResult<OrderCreatedResult>.Failure($"Order creation failed: {ex.Message}");
        }
    }
}

public class CancelOrderHandler(
    IOrderRepository orderRepository,
    ICatgaMediator mediator,
    ILogger<CancelOrderHandler> logger) : IRequestHandler<CancelOrderCommand>
{
    public async ValueTask<CatgaResult> HandleAsync(CancelOrderCommand request, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        if (order == null) return CatgaResult.Failure($"Order {request.OrderId} not found");
        if (order.Status == OrderStatus.Cancelled) return CatgaResult.Success();

        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = request.Reason;
        order.CancelledAt = DateTime.UtcNow;

        await orderRepository.UpdateAsync(order, ct);
        logger.LogInformation("Order {OrderId} cancelled", request.OrderId);
        await mediator.PublishAsync(new OrderCancelledEvent(request.OrderId, request.Reason, order.CancelledAt!.Value), ct);

        return CatgaResult.Success();
    }
}

public class GetOrderHandler(IOrderRepository orderRepository) : IRequestHandler<GetOrderQuery, Order?>
{
    public async ValueTask<CatgaResult<Order?>> HandleAsync(GetOrderQuery request, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        return CatgaResult<Order?>.Success(order);
    }
}

public class GetUserOrdersHandler(IOrderRepository orderRepository) : IRequestHandler<GetUserOrdersQuery, List<Order>>
{
    public async ValueTask<CatgaResult<List<Order>>> HandleAsync(GetUserOrdersQuery request, CancellationToken ct = default)
    {
        var orders = await orderRepository.GetByCustomerIdAsync(request.CustomerId, ct);
        return CatgaResult<List<Order>>.Success(orders);
    }
}

// ============================================
// Event Handlers (Pub/Sub)
// ============================================

public class OrderCreatedEventHandler(ILogger<OrderCreatedEventHandler> logger) : IEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Order created: {OrderId}, Customer: {CustomerId}, Amount: {Amount:C}",
            @event.OrderId, @event.CustomerId, @event.TotalAmount);
        return ValueTask.CompletedTask;
    }
}

public class SendOrderNotificationHandler(ILogger<SendOrderNotificationHandler> logger) : IEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Notification sent for order {OrderId} to customer {CustomerId}", @event.OrderId, @event.CustomerId);
        return ValueTask.CompletedTask;
    }
}

public class OrderCancelledEventHandler(ILogger<OrderCancelledEventHandler> logger) : IEventHandler<OrderCancelledEvent>
{
    public ValueTask HandleAsync(OrderCancelledEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Order cancelled: {OrderId}, Reason: {Reason}", @event.OrderId, @event.Reason ?? "Not specified");
        return ValueTask.CompletedTask;
    }
}

public class OrderConfirmedEventHandler(ILogger<OrderConfirmedEventHandler> logger) : IEventHandler<OrderConfirmedEvent>
{
    public ValueTask HandleAsync(OrderConfirmedEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Order confirmed: {OrderId}", @event.OrderId);
        return ValueTask.CompletedTask;
    }
}

// ============================================
// Pipeline Behaviors (Cross-cutting concerns)
// ============================================

/// <summary>Validates requests before processing.</summary>
public class ValidationBehavior<TRequest, TResponse>(
    ILogger<ValidationBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogDebug("Validating {Request}", requestName);

        if (request is null) return CatgaResult<TResponse>.Failure("Request cannot be null");

        var result = await next();
        if (!result.IsSuccess) logger.LogWarning("{Request} failed: {Error}", requestName, result.Error);

        return result;
    }
}

/// <summary>Logs request timing.</summary>
public class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct = default)
    {
        var requestName = typeof(TRequest).Name;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        logger.LogInformation("Handling {Request}", requestName);

        var result = await next();

        sw.Stop();
        logger.LogInformation("{Request} completed in {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);
        return result;
    }
}
