using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Runtime.InteropServices;
using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.Exceptions;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Infrastructure.Telemetry;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

public partial class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private static readonly Counter<long> _orderCreatedCounter =
        Telemetry.Meter.CreateCounter<long>("order.created", "number", "Number of orders created");
    private static readonly Histogram<double> _orderProcessingTime =
        Telemetry.Meter.CreateHistogram<double>("order.processing_time", "ms", "Order processing time in milliseconds");

    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _orderLocks = new();

    public CreateOrderHandler(
        IOrderRepository orderRepository,
        IInventoryService inventoryService,
        IPaymentService paymentService,
        ICatgaMediator mediator,
        ILogger<CreateOrderHandler> logger)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        // Compute totals without LINQ to avoid iterator allocations
        var itemsSpan = CollectionsMarshal.AsSpan(request.Items);
        decimal totalAmount = 0;
        for (int i = 0; i < itemsSpan.Length; i++)
            totalAmount += itemsSpan[i].Subtotal;

        using var activity = Telemetry.ActivitySource.StartActivityWithTags(
            "CreateOrder",
            itemCount: request.Items.Count,
            amount: totalAmount,
            paymentMethod: request.PaymentMethod);

        using var timer = _orderProcessingTime.Measure();

        try
        {
            LogOrderCreationStarted(_logger, request.CustomerId);

            // Use a per-customer lock to prevent duplicate orders
            var orderLock = _orderLocks.GetOrAdd(request.CustomerId, _ => new SemaphoreSlim(1, 1));
            await orderLock.WaitAsync(cancellationToken);

            try
            {
                // 1. Check inventory
                var stockCheck = await _inventoryService.CheckStockAsync(request.Items, cancellationToken);
                if (!stockCheck.IsSuccess)
                {
                    LogStockCheckFailed(_logger, request.CustomerId);
                    // Build product id list without LINQ allocations
                    var sb = new StringBuilder();
                    var span = CollectionsMarshal.AsSpan(request.Items);
                    for (int i = 0; i < span.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(span[i].ProductId);
                    }
                    return CatgaResult<OrderCreatedResult>.Failure($"Insufficient stock: {sb}");
                }

                // 2. Create order
                var order = new Order
                {
                    OrderId = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}",
                    CustomerId = request.CustomerId,
                    Items = request.Items,
                    TotalAmount = totalAmount,
                    Status = OrderStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    ShippingAddress = request.ShippingAddress,
                    PaymentMethod = request.PaymentMethod
                };

                // 3. Save order
                await _orderRepository.SaveAsync(order, cancellationToken);
                LogOrderSaved(_logger, order.OrderId);

                try
                {
                    // 4. Reserve inventory
                    var reserveResult = await _inventoryService.ReserveStockAsync(order.OrderId, request.Items, cancellationToken);
                    if (!reserveResult.IsSuccess)
                    {
                        await HandleOrderFailure(order, "inventory_reservation_failed", cancellationToken);
                        return CatgaResult<OrderCreatedResult>.Failure(
                            "Failed to reserve inventory. Order has been cancelled.");
                    }

                    // 5. Process payment
                    var paymentResult = await _paymentService.ProcessPaymentAsync(
                        order.OrderId,
                        totalAmount,
                        request.PaymentMethod,
                        cancellationToken);

                    if (!paymentResult.IsSuccess)
                    {
                        await HandleOrderFailure(order, "payment_failed", cancellationToken);
                        return CatgaResult<OrderCreatedResult>.Failure(
                            $"Payment failed: {paymentResult.Error}");
                    }

                    // 6. Update order status
                    order.Status = OrderStatus.Confirmed;
                    order.UpdatedAt = DateTime.UtcNow;
                    await _orderRepository.UpdateAsync(order, cancellationToken);

                    // 7. Publish event
                    await _mediator.PublishAsync(new OrderCreatedEvent(
                        order.OrderId,
                        order.CustomerId,
                        order.Items,
                        order.TotalAmount,
                        order.CreatedAt),
                        cancellationToken);

                    // 8. Record metrics
                    var tags = new TagList
                    {
                        new("status", "success"),
                        new("payment_method", request.PaymentMethod),
                        new("item_count", request.Items.Count)
                    };
                    _orderCreatedCounter.Add(1, tags);

                    LogOrderCreatedSuccess(_logger, order.OrderId, totalAmount);

                    return CatgaResult<OrderCreatedResult>.Success(
                        new OrderCreatedResult(order.OrderId, totalAmount, order.CreatedAt));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing order {OrderId}", order.OrderId);
                    await HandleOrderFailure(order, "processing_error", cancellationToken);
                    throw;
                }
            }
            finally
            {
                orderLock.Release();
                _orderLocks.TryRemove(request.CustomerId, out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating order for customer {CustomerId}", request.CustomerId);
            return CatgaResult<OrderCreatedResult>.Failure(
                $"An unexpected error occurred: {ex.Message}");
        }
    }

    private async Task HandleOrderFailure(Order order, string failureReason, CancellationToken cancellationToken)
    {
        try
        {
            // Update order status
            order.Status = OrderStatus.Failed;
            order.FailureReason = failureReason;
            order.UpdatedAt = DateTime.UtcNow;
            await _orderRepository.UpdateAsync(order, cancellationToken);

            // Release any reserved inventory
            if (failureReason != "inventory_reservation_failed")
            {
                await _inventoryService.ReleaseStockAsync(
                    order.OrderId,
                    order.Items,
                    cancellationToken);
            }

            // Publish failure event
            await _mediator.PublishAsync(new OrderFailedEvent(
                order.OrderId,
                order.CustomerId,
                failureReason,
                DateTime.UtcNow),
                cancellationToken);

            // Record failure metric
            var tags = new TagList
            {
                new("status", "failed"),
                new("reason", failureReason)
            };
            _orderCreatedCounter.Add(1, tags);

            LogOrderFailed(_logger, order.OrderId, failureReason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling order failure for {OrderId}", order.OrderId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting order creation for customer {CustomerId}")]
    static partial void LogOrderCreationStarted(ILogger logger, string customerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stock check failed for customer {CustomerId}")]
    static partial void LogStockCheckFailed(ILogger logger, string customerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} saved successfully")]
    static partial void LogOrderSaved(ILogger logger, string orderId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "✅ Order {OrderId} created successfully with total amount {Amount:C}")]
    static partial void LogOrderCreatedSuccess(ILogger logger, string orderId, decimal amount);

    [LoggerMessage(Level = LogLevel.Error, Message = "❌ Order {OrderId} failed: {Reason}")]
    static partial void LogOrderFailed(ILogger logger, string orderId, string reason);
}

public partial class CancelOrderHandler : IRequestHandler<CancelOrderCommand>
{
    private static readonly Counter<long> _orderCancelledCounter =
        Telemetry.Meter.CreateCounter<long>("order.cancelled", "number", "Number of orders cancelled");

    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryService _inventoryService;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CancelOrderHandler> _logger;

    public CancelOrderHandler(
        IOrderRepository orderRepository,
        IInventoryService inventoryService,
        ICatgaMediator mediator,
        ILogger<CancelOrderHandler> logger)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CatgaResult> HandleAsync(
        CancelOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivityWithTags(
            "CancelOrder",
            orderId: request.OrderId);

        try
        {
            var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
            if (order == null)
            {
                _logger.LogWarning("Attempted to cancel non-existent order {OrderId}", request.OrderId);
                return CatgaResult.Failure($"Order {request.OrderId} not found");
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                _logger.LogInformation("Order {OrderId} is already cancelled", request.OrderId);
                return CatgaResult.Success();
            }

            // Capture prior status to decide whether to release inventory
            var wasConfirmedOrPending = order.Status == OrderStatus.Confirmed || order.Status == OrderStatus.Pending;

            // Update order status
            order.Status = OrderStatus.Cancelled;
            order.CancellationReason = request.Reason;
            order.CancelledAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;

            await _orderRepository.UpdateAsync(order, cancellationToken);

            // Release reserved inventory if previously confirmed or pending
            if (wasConfirmedOrPending)
            {
                await _inventoryService.ReleaseStockAsync(
                    order.OrderId,
                    order.Items,
                    cancellationToken);
            }

            // Publish event
            await _mediator.PublishAsync(new OrderCancelledEvent(
                order.OrderId,
                request.Reason,
                DateTime.UtcNow),
                cancellationToken);

            // Record metric
            var tags = new TagList { new("reason", request.Reason ?? "unknown") };
            _orderCancelledCounter.Add(1, tags);

            _logger.LogInformation("✅ Order {OrderId} cancelled: {Reason}",
                request.OrderId, request.Reason);

            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error cancelling order {OrderId}", request.OrderId);
            return CatgaResult.Failure($"Error cancelling order: {ex.Message}");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} cancelled: {Reason}")]
    static partial void LogOrderCancelled(ILogger logger, string orderId, string reason);
}
