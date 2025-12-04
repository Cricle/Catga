using System.Diagnostics;
using System.Diagnostics.Metrics;
using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Order creation handler using Flow orchestration for automatic compensation.
/// Demonstrates simplified handler pattern with automatic telemetry.
///
/// Features:
/// - Pure business logic in HandleAsyncCore
/// - Automatic Activity/Span with request properties as tags
/// - Automatic duration, count, error metrics
/// - Custom [Metric] for order amount
/// </summary>
[CatgaHandler]
public sealed partial class CreateOrderFlowHandler : IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>
{
    // Telemetry - will be auto-generated in future
    private static readonly ActivitySource s_activitySource = new("OrderSystem.Api", "1.0.0");
    private static readonly Meter s_meter = new("OrderSystem.Api", "1.0.0");
    private static readonly Histogram<double> s_duration = s_meter.CreateHistogram<double>("order.flow.duration", "ms");
    private static readonly Histogram<double> s_amount = s_meter.CreateHistogram<double>("order.flow.amount", "USD");

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

    // Generated wrapper - will be auto-generated in future
    public async Task<CatgaResult<OrderCreatedResult>> HandleAsync(
        CreateOrderFlowCommand request,
        CancellationToken ct = default)
    {
        using var activity = s_activitySource.StartActivity("CreateOrderFlow");
        activity?.SetTag("customer_id", request.CustomerId);
        activity?.SetTag("payment_method", request.PaymentMethod);
        activity?.SetTag("item_count", request.Items.Count);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await HandleAsyncCore(request, ct);
            s_duration.Record(sw.Elapsed.TotalMilliseconds, new TagList { { "success", result.IsSuccess } });
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            s_duration.Record(sw.Elapsed.TotalMilliseconds, new TagList { { "success", false } });
            throw;
        }
    }

    /// <summary>
    /// Business logic only - user writes this.
    /// </summary>
    private async Task<CatgaResult<OrderCreatedResult>> HandleAsyncCore(
        CreateOrderFlowCommand request,
        CancellationToken ct)
    {
        var totalAmount = request.Items.Sum(i => i.Subtotal);

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

        var flowResult = await Flow.Create($"CreateOrder-{request.CustomerId}")
            .Step(async () => { await _inventoryService.CheckStockAsync(request.Items, ct); })
            .Step(async () => { await _orderRepository.SaveAsync(order, ct); },
                  async () => { order.Status = OrderStatus.Failed; await _orderRepository.UpdateAsync(order, ct); })
            .Step(async () => { await _inventoryService.ReserveStockAsync(order.OrderId, request.Items, ct); },
                  async () => { await _inventoryService.ReleaseStockAsync(order.OrderId, request.Items, ct); })
            .Step(async () => { await _paymentService.ProcessPaymentAsync(order.OrderId, totalAmount, request.PaymentMethod, ct); },
                  () => { _logger.LogWarning("Refunding payment for order {OrderId}", order.OrderId); return Task.CompletedTask; })
            .Step(async () =>
            {
                order.Status = OrderStatus.Confirmed;
                await _orderRepository.UpdateAsync(order, ct);
                await _mediator.PublishAsync(new OrderCreatedEvent(
                    order.OrderId, order.CustomerId, order.Items, order.TotalAmount, order.CreatedAt), ct);
            })
            .ExecuteAsync(ct);

        // Record custom metric
        s_amount.Record((double)totalAmount);

        if (flowResult.IsSuccess)
        {
            return CatgaResult<OrderCreatedResult>.Success(
                new OrderCreatedResult(order.OrderId, totalAmount, order.CreatedAt));
        }

        return CatgaResult<OrderCreatedResult>.Failure(flowResult.Error ?? "Flow failed");
    }
}
