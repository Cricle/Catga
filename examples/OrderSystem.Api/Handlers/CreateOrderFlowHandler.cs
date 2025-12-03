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
/// Demonstrates the simple, fluent Flow API with automatic rollback on failure.
///
/// Flow steps:
/// 1. Check Inventory (read-only, no compensation)
/// 2. Create Order → Compensation: Mark as Failed
/// 3. Reserve Inventory → Compensation: Release Inventory
/// 4. Process Payment → Compensation: Refund Payment
/// 5. Confirm Order (final step)
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
        CancellationToken ct = default)
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

        // Create order entity
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

        // Simple Flow API with automatic compensation
        var flowResult = await Flow.Create($"CreateOrder-{request.CustomerId}")
            .Step(async () => await _inventoryService.CheckStockAsync(request.Items, ct))
            .Step(async () => { await _orderRepository.SaveAsync(order, ct); return order; },
                async _ => { order.Status = OrderStatus.Failed; await _orderRepository.UpdateAsync(order, ct); })
            .Step(async () => await _inventoryService.ReserveStockAsync(order.OrderId, request.Items, ct),
                async _ => await _inventoryService.ReleaseStockAsync(order.OrderId, request.Items, ct))
            .Step(async () => await _paymentService.ProcessPaymentAsync(order.OrderId, totalAmount, request.PaymentMethod, ct),
                async _ => _logger.LogWarning("🔄 Refunding payment for order {OrderId}", order.OrderId))
            .Step(async () =>
            {
                order.Status = OrderStatus.Confirmed;
                await _orderRepository.UpdateAsync(order, ct);
                await _mediator.PublishAsync(new OrderCreatedEvent(
                    order.OrderId, order.CustomerId, order.Items, order.TotalAmount, order.CreatedAt), ct);
                return new OrderCreatedResult(order.OrderId, totalAmount, order.CreatedAt);
            })
            .ExecuteAsync<OrderCreatedResult>(ct);

        if (flowResult.IsSuccess)
        {
            LogFlowSuccess(_logger, order.OrderId, totalAmount);
            _orderCreatedCounter.Add(1, new TagList { new("status", "success") });
            return CatgaResult<OrderCreatedResult>.Success(flowResult.Value!);
        }

        LogFlowFailed(_logger, request.CustomerId, flowResult.CompletedSteps, flowResult.Error ?? "Unknown");
        _orderCreatedCounter.Add(1, new TagList { new("status", "failed") });
        return CatgaResult<OrderCreatedResult>.Failure(flowResult.Error ?? "Flow failed");
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "🚀 Starting order Flow for customer {CustomerId} with {ItemCount} items")]
    static partial void LogFlowStarted(ILogger logger, string customerId, int itemCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "✅ Order Flow completed: {OrderId}, Amount: {Amount:C}")]
    static partial void LogFlowSuccess(ILogger logger, string orderId, decimal amount);

    [LoggerMessage(Level = LogLevel.Error, Message = "❌ Order Flow failed for {CustomerId} at step {Step}: {Error}")]
    static partial void LogFlowFailed(ILogger logger, string customerId, int step, string error);
}
