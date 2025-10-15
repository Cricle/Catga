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
/// Create order handler - inherits SafeRequestHandler, no try-catch needed
/// </summary>
public class CreateOrderHandler : SafeRequestHandler<CreateOrderCommand, OrderCreatedResult>
{
    private readonly IOrderRepository _repository;
    private readonly IInventoryService _inventory;
    private readonly ICatgaMediator _mediator;

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
    /// </summary>
    protected override async Task<OrderCreatedResult> HandleCoreAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Check inventory
        var stockCheck = await _inventory.CheckStockAsync(request.Items, cancellationToken);
        if (!stockCheck.IsSuccess)
        {
            // Throw business exception, framework auto-converts to CatgaResult.Failure
            throw new CatgaException("Insufficient stock", stockCheck.Exception!);
        }

        // 2. Calculate total
        var totalAmount = request.Items.Sum(item => item.Subtotal);

        // 3. Create order
        var orderId = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N[..8]}";
        var order = new Order
        {
            OrderId = orderId,
            CustomerId = request.CustomerId,
            Items = request.Items,
            TotalAmount = totalAmount,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ShippingAddress = request.ShippingAddress,
            PaymentMethod = request.PaymentMethod
        };

        // 4. Save order
        await _repository.SaveAsync(order, cancellationToken);

        // 5. Reserve inventory
        await _inventory.ReserveStockAsync(orderId, request.Items, cancellationToken);

        // 6. Publish event
        await _mediator.PublishAsync(new OrderCreatedEvent(
            orderId,
            request.CustomerId,
            request.Items,
            totalAmount,
            order.CreatedAt
        ), cancellationToken);

        Logger.LogInformation("Order created: {OrderId}, amount: {Amount}", orderId, totalAmount);

        // Return result directly, no CatgaResult wrapping needed!
        return new OrderCreatedResult(
            orderId,
            totalAmount,
            order.CreatedAt
        );
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

