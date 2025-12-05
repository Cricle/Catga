using Catga;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

public class CreateOrderHandler(
    IOrderRepository orderRepository,
    IInventoryService inventoryService,
    IPaymentService paymentService,
    ICatgaMediator mediator,
    ILogger<CreateOrderHandler> logger) : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{

    public async Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        var totalAmount = request.Items.Sum(i => i.Subtotal);

        try
        {
            logger.LogInformation("Creating order for customer {CustomerId}", request.CustomerId);
            // 1. Check inventory
            var stockCheck = await inventoryService.CheckStockAsync(request.Items, cancellationToken);
            if (!stockCheck.IsSuccess)
            {
                var products = string.Join(", ", request.Items.Select(i => i.ProductId));
                return CatgaResult<OrderCreatedResult>.Failure($"Insufficient stock: {products}");
            }

            // 2. Create order
            var order = new Order
            {
                OrderId = $"ORD-{Guid.NewGuid():N}"[..20],
                CustomerId = request.CustomerId,
                Items = request.Items,
                TotalAmount = totalAmount,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                ShippingAddress = request.ShippingAddress,
                PaymentMethod = request.PaymentMethod
            };

            // 3. Save order
            await orderRepository.SaveAsync(order, cancellationToken);

            try
            {
                // 4. Reserve inventory
                var reserveResult = await inventoryService.ReserveStockAsync(order.OrderId, request.Items, cancellationToken);
                if (!reserveResult.IsSuccess)
                {
                    await HandleOrderFailure(order, "inventory_reservation_failed", cancellationToken);
                    return CatgaResult<OrderCreatedResult>.Failure("Failed to reserve inventory.");
                }

                // 5. Process payment
                var paymentResult = await paymentService.ProcessPaymentAsync(
                    order.OrderId, totalAmount, request.PaymentMethod, cancellationToken);

                if (!paymentResult.IsSuccess)
                {
                    await HandleOrderFailure(order, "payment_failed", cancellationToken);
                    return CatgaResult<OrderCreatedResult>.Failure($"Payment failed: {paymentResult.Error}");
                }

                // 6. Update order status
                order.Status = OrderStatus.Confirmed;
                order.UpdatedAt = DateTime.UtcNow;
                await orderRepository.UpdateAsync(order, cancellationToken);

                // 7. Publish event
                await mediator.PublishAsync(new OrderCreatedEvent(
                    order.OrderId, order.CustomerId, order.Items, order.TotalAmount, order.CreatedAt),
                    cancellationToken);

                logger.LogInformation("Order {OrderId} created, total: {Amount:C}", order.OrderId, totalAmount);
                return CatgaResult<OrderCreatedResult>.Success(
                    new OrderCreatedResult(order.OrderId, totalAmount, order.CreatedAt));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing order {OrderId}", order.OrderId);
                await HandleOrderFailure(order, "processing_error", cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error creating order for customer {CustomerId}", request.CustomerId);
            return CatgaResult<OrderCreatedResult>.Failure($"An unexpected error occurred: {ex.Message}");
        }
    }

    private async Task HandleOrderFailure(Order order, string failureReason, CancellationToken cancellationToken)
    {
        try
        {
            order.Status = OrderStatus.Failed;
            order.FailureReason = failureReason;
            order.UpdatedAt = DateTime.UtcNow;
            await orderRepository.UpdateAsync(order, cancellationToken);

            if (failureReason != "inventory_reservation_failed")
                await inventoryService.ReleaseStockAsync(order.OrderId, order.Items, cancellationToken);

            await mediator.PublishAsync(new OrderFailedEvent(
                order.OrderId, order.CustomerId, failureReason, DateTime.UtcNow), cancellationToken);

            logger.LogError("Order {OrderId} failed: {Reason}", order.OrderId, failureReason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling order failure for {OrderId}", order.OrderId);
        }
    }
}

public class CancelOrderHandler(
    IOrderRepository orderRepository,
    IInventoryService inventoryService,
    ICatgaMediator mediator,
    ILogger<CancelOrderHandler> logger) : IRequestHandler<CancelOrderCommand>
{
    public async Task<CatgaResult> HandleAsync(
        CancelOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
            if (order == null)
                return CatgaResult.Failure($"Order {request.OrderId} not found");

            if (order.Status == OrderStatus.Cancelled)
                return CatgaResult.Success();

            order.Status = OrderStatus.Cancelled;
            order.CancellationReason = request.Reason;
            order.CancelledAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;

            await orderRepository.UpdateAsync(order, cancellationToken);

            if (order.Status == OrderStatus.Confirmed || order.Status == OrderStatus.Pending)
                await inventoryService.ReleaseStockAsync(order.OrderId, order.Items, cancellationToken);

            await mediator.PublishAsync(new OrderCancelledEvent(
                order.OrderId, request.Reason, DateTime.UtcNow), cancellationToken);

            logger.LogInformation("Order {OrderId} cancelled", request.OrderId);
            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cancelling order {OrderId}", request.OrderId);
            return CatgaResult.Failure($"Error cancelling order: {ex.Message}");
        }
    }
}
