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
/// Order creation handler using Flow orchestration.
///
/// User only writes HandleAsyncCore - framework auto-generates:
/// - HandleAsync wrapper with telemetry
/// - Activity/Span with request properties as tags
/// - Duration, count, error metrics
/// </summary>
[CatgaHandler]
[Metric("order.flow.amount", Type = MetricType.Histogram, Unit = "USD")]
public sealed partial class CreateOrderFlowHandler : IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>
{
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

    /// <summary>
    /// Pure business logic - telemetry wrapper is auto-generated.
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

        var flowResult = await Flow.Create($"CreateOrder-{order.OrderId}")
            .Step(async () => { await _inventoryService.CheckStockAsync(request.Items, ct); })
            .Step(async () => { await _orderRepository.SaveAsync(order, ct); },
                  async () => { order.Status = OrderStatus.Failed; await _orderRepository.UpdateAsync(order, ct); })
            .Step(async () => { await _inventoryService.ReserveStockAsync(order.OrderId, request.Items, ct); },
                  async () => { await _inventoryService.ReleaseStockAsync(order.OrderId, request.Items, ct); })
            .Step(async () => { await _paymentService.ProcessPaymentAsync(order.OrderId, totalAmount, request.PaymentMethod, ct); },
                  () => { _logger.LogWarning("Refunding order {OrderId}", order.OrderId); return Task.CompletedTask; })
            .Step(async () =>
            {
                order.Status = OrderStatus.Confirmed;
                await _orderRepository.UpdateAsync(order, ct);
                await _mediator.PublishAsync(new OrderCreatedEvent(
                    order.OrderId, order.CustomerId, order.Items, order.TotalAmount, order.CreatedAt), ct);
            })
            .ExecuteAsync(ct);

        return flowResult.IsSuccess
            ? CatgaResult<OrderCreatedResult>.Success(new OrderCreatedResult(order.OrderId, totalAmount, order.CreatedAt))
            : CatgaResult<OrderCreatedResult>.Failure(flowResult.Error ?? "Flow failed");
    }
}
