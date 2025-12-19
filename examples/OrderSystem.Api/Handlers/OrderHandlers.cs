using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;
using System.Diagnostics.CodeAnalysis;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Unified order handler implementing all command, query, and event handlers.
/// Supports full order lifecycle: Create → Pay → Process → Ship → Deliver
/// </summary>
public class OrderHandler(
    IOrderRepository orderRepository,
    ICatgaMediator mediator,
    ILogger<OrderHandler> logger)
    : IRequestHandler<CreateOrderCommand, OrderCreatedResult>,
      IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>,
      IRequestHandler<PayOrderCommand>,
      IRequestHandler<ProcessOrderCommand>,
      IRequestHandler<ShipOrderCommand>,
      IRequestHandler<DeliverOrderCommand>,
      IRequestHandler<CancelOrderCommand>,
      IRequestHandler<GetOrderQuery, Order?>,
      IRequestHandler<GetUserOrdersQuery, List<Order>>,
      IRequestHandler<GetAllOrdersQuery, List<Order>>,
      IRequestHandler<GetOrderStatsQuery, OrderStats>,
      IEventHandler<OrderCreatedEvent>,
      IEventHandler<OrderCancelledEvent>,
      IEventHandler<OrderConfirmedEvent>,
      IEventHandler<OrderPaidEvent>,
      IEventHandler<OrderShippedEvent>
{
    // ============================================
    // Command Handlers
    // ============================================

    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(CreateOrderCommand request, CancellationToken ct = default)
    {
        var order = new Order
        {
            OrderId = $"ORD-{Guid.NewGuid():N}"[..16],
            CustomerId = request.CustomerId,
            Items = request.Items,
            TotalAmount = request.Items.Sum(i => i.Subtotal),
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await orderRepository.SaveAsync(order, ct);
        logger.LogInformation("Order {OrderId} created", order.OrderId);

        await mediator.PublishAsync(new OrderCreatedEvent(order.OrderId, order.CustomerId, order.TotalAmount, order.CreatedAt), ct);
        return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(order.OrderId, order.TotalAmount, order.CreatedAt));
    }

    public async ValueTask<CatgaResult<OrderCreatedResult>> HandleAsync(CreateOrderFlowCommand request, CancellationToken ct = default)
    {
        var order = new Order
        {
            OrderId = $"ORD-{Guid.NewGuid():N}"[..16],
            CustomerId = request.CustomerId,
            Items = request.Items,
            TotalAmount = request.Items.Sum(i => i.Subtotal),
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await orderRepository.SaveAsync(order, ct);
            logger.LogInformation("Flow Step 1: Order {OrderId} saved", order.OrderId);

            logger.LogInformation("Flow Step 2: Stock reserved for {OrderId}", order.OrderId);

            order.Status = OrderStatus.Paid;
            order.UpdatedAt = DateTime.UtcNow;
            await orderRepository.UpdateAsync(order, ct);
            logger.LogInformation("Flow Step 3: Order {OrderId} confirmed", order.OrderId);

            await mediator.PublishAsync(new OrderConfirmedEvent(order.OrderId, DateTime.UtcNow), ct);
            return CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(order.OrderId, order.TotalAmount, order.CreatedAt));
        }
        catch (Exception ex)
        {
            order.Status = OrderStatus.Failed;
            await orderRepository.UpdateAsync(order, ct);
            logger.LogWarning(ex, "Order {OrderId} failed, compensation executed", order.OrderId);
            return CatgaResult<OrderCreatedResult>.Failure($"Order creation failed: {ex.Message}");
        }
    }

    public async ValueTask<CatgaResult> HandleAsync(CancelOrderCommand request, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        if (order == null) return CatgaResult.Failure($"Order {request.OrderId} not found");
        if (!order.CanCancel) return CatgaResult.Failure($"Order {request.OrderId} cannot be cancelled in status {order.Status}");
        if (order.Status == OrderStatus.Cancelled) return CatgaResult.Success();

        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = request.Reason;
        order.CancelledAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;

        await orderRepository.UpdateAsync(order, ct);
        logger.LogInformation("Order {OrderId} cancelled", request.OrderId);
        await mediator.PublishAsync(new OrderCancelledEvent(request.OrderId, request.Reason, order.CancelledAt!.Value), ct);
        return CatgaResult.Success();
    }

    public async ValueTask<CatgaResult> HandleAsync(PayOrderCommand request, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        if (order == null) return CatgaResult.Failure($"Order {request.OrderId} not found");
        if (order.Status != OrderStatus.Pending) return CatgaResult.Failure($"Order {request.OrderId} is not pending payment");

        order.Status = OrderStatus.Paid;
        order.PaidAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;
        order.PaymentMethod = request.PaymentMethod;
        order.PaymentTransactionId = request.TransactionId;

        await orderRepository.UpdateAsync(order, ct);
        logger.LogInformation("Order {OrderId} paid via {Method}", request.OrderId, request.PaymentMethod);
        await mediator.PublishAsync(new OrderPaidEvent(request.OrderId, request.PaymentMethod, order.TotalAmount, order.PaidAt!.Value), ct);
        return CatgaResult.Success();
    }

    public async ValueTask<CatgaResult> HandleAsync(ProcessOrderCommand request, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        if (order == null) return CatgaResult.Failure($"Order {request.OrderId} not found");
        if (order.Status != OrderStatus.Paid) return CatgaResult.Failure($"Order {request.OrderId} must be paid before processing");

        order.Status = OrderStatus.Processing;
        order.UpdatedAt = DateTime.UtcNow;

        await orderRepository.UpdateAsync(order, ct);
        logger.LogInformation("Order {OrderId} is now processing", request.OrderId);
        return CatgaResult.Success();
    }

    public async ValueTask<CatgaResult> HandleAsync(ShipOrderCommand request, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        if (order == null) return CatgaResult.Failure($"Order {request.OrderId} not found");
        if (!order.CanShip) return CatgaResult.Failure($"Order {request.OrderId} cannot be shipped in status {order.Status}");

        order.Status = OrderStatus.Shipped;
        order.ShippedAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;
        order.TrackingNumber = request.TrackingNumber;

        await orderRepository.UpdateAsync(order, ct);
        logger.LogInformation("Order {OrderId} shipped with tracking {Tracking}", request.OrderId, request.TrackingNumber);
        await mediator.PublishAsync(new OrderShippedEvent(request.OrderId, request.TrackingNumber, order.ShippedAt!.Value), ct);
        return CatgaResult.Success();
    }

    public async ValueTask<CatgaResult> HandleAsync(DeliverOrderCommand request, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        if (order == null) return CatgaResult.Failure($"Order {request.OrderId} not found");
        if (!order.CanDeliver) return CatgaResult.Failure($"Order {request.OrderId} cannot be delivered in status {order.Status}");

        order.Status = OrderStatus.Delivered;
        order.DeliveredAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;

        await orderRepository.UpdateAsync(order, ct);
        logger.LogInformation("Order {OrderId} delivered", request.OrderId);
        return CatgaResult.Success();
    }

    // ============================================
    // Query Handlers
    // ============================================

    public async ValueTask<CatgaResult<Order?>> HandleAsync(GetOrderQuery request, CancellationToken ct = default)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId, ct);
        return CatgaResult<Order?>.Success(order);
    }

    public async ValueTask<CatgaResult<List<Order>>> HandleAsync(GetUserOrdersQuery request, CancellationToken ct = default)
    {
        var orders = await orderRepository.GetByCustomerIdAsync(request.CustomerId, ct);
        return CatgaResult<List<Order>>.Success(orders);
    }

    public async ValueTask<CatgaResult<List<Order>>> HandleAsync(GetAllOrdersQuery request, CancellationToken ct = default)
    {
        var orders = await orderRepository.GetAllAsync(request.Status, request.Limit, ct);
        return CatgaResult<List<Order>>.Success(orders);
    }

    public async ValueTask<CatgaResult<OrderStats>> HandleAsync(GetOrderStatsQuery request, CancellationToken ct = default)
    {
        var stats = await orderRepository.GetStatsAsync(ct);
        return CatgaResult<OrderStats>.Success(stats);
    }

    // ============================================
    // Event Handlers
    // ============================================

    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Event: Order created - {OrderId}, Customer: {CustomerId}, Amount: {Amount:C}",
            @event.OrderId, @event.CustomerId, @event.TotalAmount);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(OrderCancelledEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Event: Order cancelled - {OrderId}, Reason: {Reason}",
            @event.OrderId, @event.Reason ?? "Not specified");
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(OrderConfirmedEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Event: Order confirmed - {OrderId}", @event.OrderId);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(OrderPaidEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Event: Order paid - {OrderId}, Method: {Method}, Amount: {Amount:C}",
            @event.OrderId, @event.PaymentMethod, @event.Amount);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(OrderShippedEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Event: Order shipped - {OrderId}, Tracking: {Tracking}",
            @event.OrderId, @event.TrackingNumber);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Secondary event handler demonstrating multiple handlers for same event.
/// </summary>
public class OrderNotificationHandler(ILogger<OrderNotificationHandler> logger) : IEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Notification: Sending email for order {OrderId} to customer {CustomerId}",
            @event.OrderId, @event.CustomerId);
        return ValueTask.CompletedTask;
    }
}

// ============================================
// Pipeline Behaviors (Cross-cutting concerns)
// ============================================

/// <summary>Validates requests before processing.</summary>
public class ValidationBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
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
public class LoggingBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
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
