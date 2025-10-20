using Catga;
using Catga.Core;
using Catga.Exceptions;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

public partial class CreateOrderHandler : SafeRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryService _inventory;
    private readonly ICatgaMediator _mediator;
    private string? _orderId;
    private bool _inventoryReserved, _orderSaved;

    public CreateOrderHandler(IOrderRepository repository, IInventoryService inventory,
        ICatgaMediator mediator, ILogger<CreateOrderHandler> logger) : base(logger)
    {
        (_repository, _inventory, _mediator) = (repository, inventory, mediator);
    }

    protected override async Task<OrderCreatedResult> HandleCoreAsync(
        CreateOrderCommand request, CancellationToken cancellationToken)
    {
        LogOrderCreationStarted(request.CustomerId);

        var stockCheck = await _inventory.CheckStockAsync(request.Items, cancellationToken);
        if (!stockCheck.IsSuccess)
        {
            LogStockCheckFailed(request.CustomerId);
            throw new CatgaException($"Insufficient stock: {string.Join(", ", request.Items.Select(i => i.ProductId))}");
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
            throw new CatgaException("Failed to reserve inventory", reserveResult.Exception!);

        _inventoryReserved = true;
        LogInventoryReserved(_orderId);

        if (request.PaymentMethod.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
            throw new CatgaException($"Payment method '{request.PaymentMethod}' validation failed");

        await _mediator.PublishAsync(new OrderCreatedEvent(
            _orderId, request.CustomerId, request.Items, totalAmount, order.CreatedAt), cancellationToken);

        LogOrderCreatedSuccess(_orderId, totalAmount);
        return new OrderCreatedResult(_orderId, totalAmount, order.CreatedAt);
    }

    protected override async Task<CatgaResult<OrderCreatedResult>> OnBusinessErrorAsync(
        CreateOrderCommand request, CatgaException exception, CancellationToken cancellationToken)
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
                    _orderId, request.CustomerId, exception.Message, DateTime.UtcNow), cancellationToken);

            LogRollbackCompleted();
        }
        catch (Exception rollbackEx)
        {
            LogRollbackFailed(rollbackEx, _orderId ?? "N/A");
        }

        var metadata = new ResultMetadata();
        metadata.Add("OrderId", _orderId ?? "N/A");
        metadata.Add("CustomerId", request.CustomerId);
        metadata.Add("RollbackCompleted", "true");
        metadata.Add("InventoryRolledBack", _inventoryReserved.ToString());
        metadata.Add("OrderDeleted", _orderSaved.ToString());
        metadata.Add("FailureTimestamp", DateTime.UtcNow.ToString("O"));

        return new CatgaResult<OrderCreatedResult>
        {
            IsSuccess = false,
            Error = $"Order creation failed: {exception.Message}. All changes rolled back.",
            Exception = exception,
            Metadata = metadata
        };
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

public partial class CancelOrderHandler : SafeRequestHandler<CancelOrderCommand>
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryService _inventory;
    private readonly ICatgaMediator _mediator;

    public CancelOrderHandler(IOrderRepository repository, IInventoryService inventory,
        ICatgaMediator mediator, ILogger<CancelOrderHandler> logger) : base(logger)
    {
        (_repository, _inventory, _mediator) = (repository, inventory, mediator);
    }

    protected override async Task HandleCoreAsync(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken)
            ?? throw new CatgaException("Order not found");

        if (order.Status == OrderStatus.Cancelled)
            throw new CatgaException("Order already cancelled");

        await _repository.UpdateAsync(order with { Status = OrderStatus.Cancelled, UpdatedAt = DateTime.UtcNow }, cancellationToken);
        await _inventory.ReleaseStockAsync(request.OrderId, order.Items, cancellationToken);
        await _mediator.PublishAsync(new OrderCancelledEvent(request.OrderId, request.Reason, DateTime.UtcNow), cancellationToken);

        LogOrderCancelled(request.OrderId, request.Reason);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order cancelled: {OrderId}, reason: {Reason}")]
    partial void LogOrderCancelled(string orderId, string reason);
}
