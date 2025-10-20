using Catga;
using Catga.Core;
using Catga.Exceptions;
using Catga.Abstractions;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;
using Catga.Abstractions;

namespace OrderSystem.Api.Handlers;

public partial class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryService _inventory;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;
    private string? _orderId;
    private bool _inventoryReserved, _orderSaved;

    public CreateOrderHandler(IOrderRepository repository, IInventoryService inventory,
        ICatgaMediator mediator, ILogger<CreateOrderHandler> logger)
    {
        (_repository, _inventory, _mediator, _logger) = (repository, inventory, mediator, logger);
    }

    public async Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            LogOrderCreationStarted(request.CustomerId);

            var stockCheck = await _inventory.CheckStockAsync(request.Items, cancellationToken);
            if (!stockCheck.IsSuccess)
            {
                LogStockCheckFailed(request.CustomerId);
                return CatgaResult<OrderCreatedResult>.Failure(
                    $"Insufficient stock: {string.Join(", ", request.Items.Select(i => i.ProductId))}");
            }

            var totalAmount = request.Items.Sum(item => item.Subtotal);
            LogOrderTotalCalculated(totalAmount);

            _orderId = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
            var order = new Order
            {
                OrderId = _orderId,
                CustomerId = request.CustomerId,
                Items = request.Items,
                TotalAmount = totalAmount,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                ShippingAddress = request.ShippingAddress,
                PaymentMethod = request.PaymentMethod
            };

            await _repository.SaveAsync(order, cancellationToken);
            _orderSaved = true;
            LogOrderSaved(_orderId);

            var reserveResult = await _inventory.ReserveStockAsync(_orderId, request.Items, cancellationToken);
            if (!reserveResult.IsSuccess)
            {
                await RollbackAsync(request, cancellationToken);
                return CatgaResult<OrderCreatedResult>.Failure(
                    $"Failed to reserve inventory. All changes rolled back.");
            }

            _inventoryReserved = true;
            LogInventoryReserved(_orderId);

            if (request.PaymentMethod.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
            {
                await RollbackAsync(request, cancellationToken);
                return CatgaResult<OrderCreatedResult>.Failure(
                    $"Payment method '{request.PaymentMethod}' validation failed. All changes rolled back.");
            }

            await _mediator.PublishAsync(new OrderCreatedEvent(
                _orderId, request.CustomerId, request.Items, totalAmount, order.CreatedAt), cancellationToken);

            LogOrderCreatedSuccess(_orderId, totalAmount);
            return CatgaResult<OrderCreatedResult>.Success(
                new OrderCreatedResult(_orderId, totalAmount, order.CreatedAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order creation failed for customer {CustomerId}", request.CustomerId);
            await RollbackAsync(request, cancellationToken);
            return CatgaResult<OrderCreatedResult>.Failure($"Order creation failed: {ex.Message}. All changes rolled back.");
        }
    }

    private async Task RollbackAsync(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        LogRollbackInitiated();

        try
        {
            if (_inventoryReserved && _orderId != null)
            {
                await _inventory.ReleaseStockAsync(_orderId, request.Items, cancellationToken);
                LogInventoryRolledBack();
            }

            if (_orderSaved && _orderId != null)
            {
                await _repository.DeleteAsync(_orderId, cancellationToken);
                LogOrderDeleted();
            }

            if (_orderId != null)
                await _mediator.PublishAsync(new OrderFailedEvent(
                    _orderId, request.CustomerId, "Rollback completed", DateTime.UtcNow), cancellationToken);

            LogRollbackCompleted();
        }
        catch (Exception rollbackEx)
        {
            LogRollbackFailed(rollbackEx, _orderId ?? "N/A");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting order creation for customer {CustomerId}")]
    partial void LogOrderCreationStarted(string customerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stock check failed for customer {CustomerId}")]
    partial void LogStockCheckFailed(string customerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order total calculated: {TotalAmount}")]
    partial void LogOrderTotalCalculated(decimal totalAmount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order saved: {OrderId}")]
    partial void LogOrderSaved(string orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inventory reserved for order {OrderId}")]
    partial void LogInventoryReserved(string orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "✅ Order created successfully: {OrderId}, Amount: {Amount}")]
    partial void LogOrderCreatedSuccess(string orderId, decimal amount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "⚠️ Order creation failed, initiating rollback...")]
    partial void LogRollbackInitiated();

    [LoggerMessage(Level = LogLevel.Information, Message = "✓ Inventory rollback completed")]
    partial void LogInventoryRolledBack();

    [LoggerMessage(Level = LogLevel.Information, Message = "✓ Order deletion completed")]
    partial void LogOrderDeleted();

    [LoggerMessage(Level = LogLevel.Information, Message = "✅ Rollback completed successfully")]
    partial void LogRollbackCompleted();

    [LoggerMessage(Level = LogLevel.Error, Message = "❌ Rollback failed! Manual intervention required for order {OrderId}")]
    partial void LogRollbackFailed(Exception exception, string orderId);
}

public partial class CancelOrderHandler : IRequestHandler<CancelOrderCommand>
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryService _inventory;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CancelOrderHandler> _logger;

    public CancelOrderHandler(IOrderRepository repository, IInventoryService inventory,
        ICatgaMediator mediator, ILogger<CancelOrderHandler> logger)
    {
        (_repository, _inventory, _mediator, _logger) = (repository, inventory, mediator, logger);
    }

    public async Task<CatgaResult> HandleAsync(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
            if (order == null)
                return CatgaResult.Failure("Order not found");

            if (order.Status == OrderStatus.Cancelled)
                return CatgaResult.Failure("Order already cancelled");

            await _repository.UpdateAsync(order with { Status = OrderStatus.Cancelled, UpdatedAt = DateTime.UtcNow }, cancellationToken);
            await _inventory.ReleaseStockAsync(request.OrderId, order.Items, cancellationToken);
            await _mediator.PublishAsync(new OrderCancelledEvent(request.OrderId, request.Reason, DateTime.UtcNow), cancellationToken);

            LogOrderCancelled(request.OrderId, request.Reason);
            return CatgaResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel order {OrderId}", request.OrderId);
            return CatgaResult.Failure($"Failed to cancel order: {ex.Message}");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order cancelled: {OrderId}, reason: {Reason}")]
    partial void LogOrderCancelled(string orderId, string reason);
}
