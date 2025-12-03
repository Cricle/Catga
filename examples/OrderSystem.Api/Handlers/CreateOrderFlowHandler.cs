using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;
using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Infrastructure.Telemetry;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Order creation handler using Flow orchestration for automatic compensation.
/// This demonstrates the zero-cost saga pattern with automatic rollback on failure.
///
/// Comparison with traditional approach (CreateOrderHandler):
/// - Traditional: Manual try-catch, explicit HandleOrderFailure calls
/// - Flow: Automatic compensation in reverse order on any failure
///
/// Flow steps:
/// 1. Create Order → Compensation: Delete Order
/// 2. Reserve Inventory → Compensation: Release Inventory
/// 3. Process Payment → Compensation: Refund Payment
/// 4. Confirm Order (no compensation needed)
/// </summary>
public sealed partial class CreateOrderFlowHandler : IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>
{
    private static readonly Counter<long> _orderCreatedCounter =
        Telemetry.Meter.CreateCounter<long>("order.flow.created", "number", "Number of orders created via Flow");
    private static readonly Histogram<double> _orderProcessingTime =
        Telemetry.Meter.CreateHistogram<double>("order.flow.processing_time", "ms", "Order Flow processing time");

    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderFlowHandler> _logger;

    public CreateOrderFlowHandler(
        IOrderRepository orderRepository,
        IInventoryService inventoryService,
        IPaymentService paymentService,
        ICatgaMediator mediator,
        ILogger<CreateOrderFlowHandler> logger)
    {
        _orderRepository = orderRepository;
        _inventoryService = inventoryService;
        _paymentService = paymentService;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderFlowCommand request,
        CancellationToken cancellationToken = default)
    {
        var itemsSpan = CollectionsMarshal.AsSpan(request.Items);
        decimal totalAmount = 0;
        for (int i = 0; i < itemsSpan.Length; i++)
            totalAmount += itemsSpan[i].Subtotal;

        using var activity = Telemetry.ActivitySource.StartActivityWithTags(
            "CreateOrderFlow",
            itemCount: request.Items.Count,
            amount: totalAmount,
            paymentMethod: request.PaymentMethod);

        using var timer = _orderProcessingTime.Measure();

        LogFlowStarted(_logger, request.CustomerId, request.Items.Count);

        // Use Flow orchestration - automatic compensation on failure
        var flowResult = await _mediator.RunFlowAsync<OrderCreatedResult>(
            $"CreateOrder-{request.CustomerId}",
            async flow =>
            {
                // Step 1: Check inventory (no compensation needed - read only)
                LogFlowStep(_logger, flow.StepCount, "CheckInventory");
                var stockCheck = await _inventoryService.CheckStockAsync(request.Items, cancellationToken);
                if (!stockCheck.IsSuccess)
                {
                    throw new FlowExecutionException("CheckInventory", "Insufficient stock", flow.StepCount);
                }

                // Step 2: Create order
                LogFlowStep(_logger, flow.StepCount, "CreateOrder");
                var order = new Order
                {
                    OrderId = $"FLOW-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..36],
                    CustomerId = request.CustomerId,
                    Items = request.Items,
                    TotalAmount = totalAmount,
                    Status = OrderStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    ShippingAddress = request.ShippingAddress,
                    PaymentMethod = request.PaymentMethod
                };
                await _orderRepository.SaveAsync(order, cancellationToken);

                // Register compensation: Delete order on failure
                var orderRepo = _orderRepository;
                var mediator = _mediator;
                var logger = _logger;
                var orderId = order.OrderId;
                var customerId = order.CustomerId;
                flow.RegisterCompensation(async ct =>
                {
                    LogCompensation(logger, "DeleteOrder", orderId);
                    order.Status = OrderStatus.Failed;
                    order.FailureReason = "Flow compensation";
                    order.UpdatedAt = DateTime.UtcNow;
                    await orderRepo.UpdateAsync(order, ct);
                    await mediator.PublishAsync(new OrderFailedEvent(
                        orderId, customerId, "Flow compensation", DateTime.UtcNow), ct);
                }, "DeleteOrder");

                // Step 3: Reserve inventory
                LogFlowStep(_logger, flow.StepCount, "ReserveInventory");
                var reserveResult = await _inventoryService.ReserveStockAsync(
                    order.OrderId, request.Items, cancellationToken);

                if (!reserveResult.IsSuccess)
                {
                    throw new FlowExecutionException("ReserveInventory",
                        reserveResult.Error ?? "Failed to reserve inventory", flow.StepCount);
                }

                // Register compensation: Release inventory on failure
                var inventoryService = _inventoryService;
                var items = request.Items;
                flow.RegisterCompensation(async ct =>
                {
                    LogCompensation(logger, "ReleaseInventory", orderId);
                    await inventoryService.ReleaseStockAsync(orderId, items, ct);
                }, "ReleaseInventory");

                // Step 4: Process payment
                LogFlowStep(_logger, flow.StepCount, "ProcessPayment");
                var paymentResult = await _paymentService.ProcessPaymentAsync(
                    order.OrderId, totalAmount, request.PaymentMethod, cancellationToken);

                if (!paymentResult.IsSuccess)
                {
                    throw new FlowExecutionException("ProcessPayment",
                        paymentResult.Error ?? "Payment failed", flow.StepCount);
                }

                // Register compensation: Refund payment on failure (for subsequent steps)
                flow.RegisterCompensation(async ct =>
                {
                    LogCompensation(logger, "RefundPayment", orderId);
                    // In real implementation, call payment gateway refund API
                    logger.LogWarning("🔄 Refunding payment for order {OrderId}", orderId);
                    await Task.CompletedTask;
                }, "RefundPayment");

                // Step 5: Confirm order (final step - no compensation needed)
                LogFlowStep(_logger, flow.StepCount, "ConfirmOrder");
                order.Status = OrderStatus.Confirmed;
                order.UpdatedAt = DateTime.UtcNow;
                await _orderRepository.UpdateAsync(order, cancellationToken);

                // Publish success event
                await _mediator.PublishAsync(new OrderCreatedEvent(
                    order.OrderId, order.CustomerId, order.Items,
                    order.TotalAmount, order.CreatedAt), cancellationToken);

                LogFlowSuccess(_logger, order.OrderId, totalAmount, flow.StepCount);

                // Record success metric
                _orderCreatedCounter.Add(1, new TagList
                {
                    new("status", "success"),
                    new("payment_method", request.PaymentMethod)
                });

                return new OrderCreatedResult(order.OrderId, totalAmount, order.CreatedAt);
            },
            cancellationToken);

        if (flowResult.IsSuccess)
        {
            return CatgaResult<OrderCreatedResult>.Success(flowResult.Value!);
        }

        // Flow failed - compensation already executed automatically
        var failedStep = flowResult.FailedAtStep.ToString();
        LogFlowFailed(_logger, request.CustomerId, failedStep, flowResult.Error ?? "Unknown");

        _orderCreatedCounter.Add(1, new TagList
        {
            new("status", "failed"),
            new("failed_step", failedStep)
        });

        return CatgaResult<OrderCreatedResult>.Failure(
            $"Order creation failed at step '{flowResult.FailedAtStep}': {flowResult.Error}");
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "🚀 Starting order Flow for customer {CustomerId} with {ItemCount} items")]
    static partial void LogFlowStarted(ILogger logger, string customerId, int itemCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "📍 Flow step {StepIndex}: {StepName}")]
    static partial void LogFlowStep(ILogger logger, int stepIndex, string stepName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "🔄 COMPENSATION: {Action} for order {OrderId}")]
    static partial void LogCompensation(ILogger logger, string action, string orderId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "✅ Order Flow completed: {OrderId}, Amount: {Amount:C}, Steps: {StepCount}")]
    static partial void LogFlowSuccess(ILogger logger, string orderId, decimal amount, int stepCount);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "❌ Order Flow failed for {CustomerId} at step '{FailedStep}': {Error}")]
    static partial void LogFlowFailed(ILogger logger, string customerId, string failedStep, string error);
}
