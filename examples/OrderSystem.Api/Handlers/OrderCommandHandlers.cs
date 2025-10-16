using Catga;
using Catga.Core;
using Catga.Exceptions;
using Catga.Handlers;
using Catga.Results;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Create order handler - demonstrates success flow and automatic rollback on failure
/// </summary>
public class CreateOrderHandler : SafeRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryService _inventory;
    private readonly ICatgaMediator _mediator;

    // Track rollback state
    private string? _orderId;
    private bool _inventoryReserved;
    private bool _orderSaved;

    public CreateOrderHandler(
        IOrderRepository repository,
        IInventoryService inventory,
        ICatgaMediator mediator,
        ILogger<CreateOrderHandler> logger) : base(logger)
    {
        _repository = repository;
        _inventory = inventory;
        _mediator = mediator;
    }

    /// <summary>
    /// Users only write business logic, no exception handling needed!
    /// Framework automatically handles errors and triggers rollback via OnBusinessErrorAsync
    /// </summary>
    protected override async Task<OrderCreatedResult> HandleCoreAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting order creation for customer {CustomerId}", request.CustomerId);

        // 1. Check inventory
        var stockCheck = await _inventory.CheckStockAsync(request.Items, cancellationToken);
        if (!stockCheck.IsSuccess)
        {
            Logger.LogWarning("Stock check failed for customer {CustomerId}", request.CustomerId);
            throw new CatgaException($"Insufficient stock for items: {string.Join(", ", request.Items.Select(i => i.ProductId))}");
        }

        // 2. Calculate total
        var totalAmount = request.Items.Sum(item => item.Subtotal);
        Logger.LogInformation("Order total calculated: {TotalAmount}", totalAmount);

        // 3. Create order
        _orderId = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N[..8]}";
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

        // 4. Save order (checkpoint 1)
        await _repository.SaveAsync(order, cancellationToken);
        _orderSaved = true;
        Logger.LogInformation("Order saved: {OrderId}", _orderId);

        // 5. Reserve inventory (checkpoint 2)
        var reserveResult = await _inventory.ReserveStockAsync(_orderId, request.Items, cancellationToken);
        if (!reserveResult.IsSuccess)
        {
            throw new CatgaException("Failed to reserve inventory", reserveResult.Exception!);
        }
        _inventoryReserved = true;
        Logger.LogInformation("Inventory reserved for order {OrderId}", _orderId);

        // 6. Validate payment method (Demo: trigger failure if contains "FAIL")
        if (request.PaymentMethod.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            throw new CatgaException($"Payment method '{request.PaymentMethod}' validation failed");
        }

        // 7. Publish event
        await _mediator.PublishAsync(new OrderCreatedEvent(
            _orderId,
            request.CustomerId,
            request.Items,
            totalAmount,
            order.CreatedAt
        ), cancellationToken);

        Logger.LogInformation("✅ Order created successfully: {OrderId}, Amount: {Amount}", _orderId, totalAmount);

        return new OrderCreatedResult(_orderId, totalAmount, order.CreatedAt);
    }

    /// <summary>
    /// Custom error handler with automatic rollback
    /// </summary>
    protected override async Task<CatgaResult<OrderCreatedResult>> OnBusinessErrorAsync(
        CreateOrderCommand request,
        CatgaException exception,
        CancellationToken cancellationToken)
    {
        Logger.LogWarning("⚠️ Order creation failed, initiating rollback...");

        try
        {
            // Rollback in reverse order
            if (_inventoryReserved && _orderId != null)
            {
                Logger.LogInformation("Rolling back inventory for order {OrderId}", _orderId);
                await _inventory.ReleaseStockAsync(_orderId, request.Items, cancellationToken);
                Logger.LogInformation("✓ Inventory rollback completed");
            }

            if (_orderSaved && _orderId != null)
            {
                Logger.LogInformation("Rolling back order {OrderId}", _orderId);
                await _repository.DeleteAsync(_orderId, cancellationToken);
                Logger.LogInformation("✓ Order deletion completed");
            }

            // Publish failure event
            if (_orderId != null)
            {
                await _mediator.PublishAsync(new OrderFailedEvent(
                    _orderId,
                    request.CustomerId,
                    exception.Message,
                    DateTime.UtcNow
                ), cancellationToken);
            }

            Logger.LogInformation("✅ Rollback completed successfully");
        }
        catch (Exception rollbackEx)
        {
            Logger.LogError(rollbackEx, "❌ Rollback failed! Manual intervention required for order {OrderId}", _orderId);
        }

        // Return detailed error with metadata
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
            Error = $"Order creation failed: {exception.Message}. All changes have been rolled back.",
            Exception = exception,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Confirm order handler - no try-catch needed
/// </summary>
public class ConfirmOrderHandler : SafeRequestHandler<ConfirmOrderCommand>
{
    private readonly IOrderRepository _repository;
    private readonly ICatgaMediator _mediator;

    public ConfirmOrderHandler(
        IOrderRepository repository,
        ICatgaMediator mediator,
        ILogger<ConfirmOrderHandler> logger) : base(logger)
    {
        _repository = repository;
        _mediator = mediator;
    }

    protected override async Task HandleCoreAsync(
        ConfirmOrderCommand request,
        CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
            throw new CatgaException("Order not found");

        if (order.Status != OrderStatus.Pending)
            throw new CatgaException($"Invalid order status: {order.Status}");

        var updatedOrder = order with
        {
            Status = OrderStatus.Confirmed,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.UpdateAsync(updatedOrder, cancellationToken);

        await _mediator.PublishAsync(new OrderConfirmedEvent(
            request.OrderId,
            DateTime.UtcNow
        ), cancellationToken);

        Logger.LogInformation("Order confirmed: {OrderId}", request.OrderId);
    }
}

/// <summary>
/// Pay order handler - no try-catch needed
/// </summary>
public class PayOrderHandler : SafeRequestHandler<PayOrderCommand>
{
    private readonly IOrderRepository _repository;
    private readonly IPaymentService _payment;
    private readonly ICatgaMediator _mediator;

    public PayOrderHandler(
        IOrderRepository repository,
        IPaymentService payment,
        ICatgaMediator mediator,
        ILogger<PayOrderHandler> logger) : base(logger)
    {
        _repository = repository;
        _payment = payment;
        _mediator = mediator;
    }

    protected override async Task HandleCoreAsync(
        PayOrderCommand request,
        CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
            throw new CatgaException("Order not found");

        if (order.Status != OrderStatus.Confirmed)
            throw new CatgaException($"Invalid order status: {order.Status}");

        // Process payment
        var paymentResult = await _payment.ProcessPaymentAsync(
            request.OrderId,
            request.Amount,
            request.PaymentMethod,
            cancellationToken);

        if (!paymentResult.IsSuccess)
            throw new CatgaException("Payment failed", paymentResult.Exception!);

        var updatedOrder = order with
        {
            Status = OrderStatus.Paid,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.UpdateAsync(updatedOrder, cancellationToken);

        await _mediator.PublishAsync(new OrderPaidEvent(
            request.OrderId,
            request.PaymentMethod,
            request.Amount,
            DateTime.UtcNow
        ), cancellationToken);

        Logger.LogInformation("Order paid: {OrderId}, amount: {Amount}", request.OrderId, request.Amount);
    }
}

/// <summary>
/// Cancel order handler - no try-catch needed
/// </summary>
public class CancelOrderHandler : SafeRequestHandler<CancelOrderCommand>
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryService _inventory;
    private readonly ICatgaMediator _mediator;

    public CancelOrderHandler(
        IOrderRepository repository,
        IInventoryService inventory,
        ICatgaMediator mediator,
        ILogger<CancelOrderHandler> logger) : base(logger)
    {
        _repository = repository;
        _inventory = inventory;
        _mediator = mediator;
    }

    protected override async Task HandleCoreAsync(
        CancelOrderCommand request,
        CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
            throw new CatgaException("Order not found");

        if (order.Status == OrderStatus.Shipped || order.Status == OrderStatus.Delivered)
            throw new CatgaException("Cannot cancel shipped order");

        var updatedOrder = order with
        {
            Status = OrderStatus.Cancelled,
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.UpdateAsync(updatedOrder, cancellationToken);

        // Release inventory
        await _inventory.ReleaseStockAsync(request.OrderId, order.Items, cancellationToken);

        await _mediator.PublishAsync(new OrderCancelledEvent(
            request.OrderId,
            request.Reason,
            DateTime.UtcNow
        ), cancellationToken);

        Logger.LogInformation("Order cancelled: {OrderId}, reason: {Reason}", request.OrderId, request.Reason);
    }
}

