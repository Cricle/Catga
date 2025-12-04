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
/// Order creation handler - user only writes business logic.
///
/// Framework auto-generates:
/// - HandleAsync (telemetry, metrics, tracing)
/// - ExecuteFlowAsync (from [FlowStep] methods)
/// - Idempotency check (Inbox pattern)
/// - Distributed lock acquisition
/// - Retry on transient failures
/// </summary>
[CatgaHandler]
[Route("/orders")]
[Idempotent(Key = "{request.CustomerId}:{request.Items.Count}")]
[DistributedLock("order:{request.CustomerId}")]
[Retry(MaxAttempts = 3)]
[Timeout(30)]
public sealed partial class CreateOrderFlowHandler(
    IOrderRepository orderRepository,
    IInventoryService inventoryService,
    IPaymentService paymentService,
    ICatgaMediator mediator,
    ILogger<CreateOrderFlowHandler> logger) : IRequestHandler<CreateOrderFlowCommand, OrderCreatedResult>
{
    private Order _order = null!;

    private async Task<CatgaResult<OrderCreatedResult>> HandleAsyncCore(
        CreateOrderFlowCommand request, CancellationToken ct)
    {
        _order = new Order
        {
            OrderId = $"ORD-{Guid.NewGuid():N}"[..16],
            CustomerId = request.CustomerId,
            Items = request.Items,
            TotalAmount = request.Items.Sum(i => i.Subtotal),
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ShippingAddress = request.ShippingAddress,
            PaymentMethod = request.PaymentMethod
        };

        var result = await ExecuteFlowAsync(ct);

        return result.IsSuccess
            ? CatgaResult<OrderCreatedResult>.Success(new(_order.OrderId, _order.TotalAmount, _order.CreatedAt))
            : CatgaResult<OrderCreatedResult>.Failure(result.Error ?? "Flow failed");
    }

    // Flow steps - pure business logic

    [FlowStep(Order = 1)]
    Task CheckInventory(CancellationToken ct)
        => inventoryService.CheckStockAsync(_order.Items, ct).AsTask();

    [FlowStep(Order = 2, Compensate = nameof(MarkFailed))]
    Task SaveOrder(CancellationToken ct)
        => orderRepository.SaveAsync(_order, ct).AsTask();

    async Task MarkFailed(CancellationToken ct)
    {
        _order.Status = OrderStatus.Failed;
        await orderRepository.UpdateAsync(_order, ct);
    }

    [FlowStep(Order = 3, Compensate = nameof(ReleaseStock))]
    Task ReserveStock(CancellationToken ct)
        => inventoryService.ReserveStockAsync(_order.OrderId, _order.Items, ct).AsTask();

    Task ReleaseStock(CancellationToken ct)
        => inventoryService.ReleaseStockAsync(_order.OrderId, _order.Items, ct).AsTask();

    [FlowStep(Order = 4, Compensate = nameof(LogRefund))]
    async Task ProcessPayment(CancellationToken ct)
    {
        var result = await paymentService.ProcessPaymentAsync(_order.OrderId, _order.TotalAmount, _order.PaymentMethod ?? "card", ct);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Error ?? "Payment failed");
    }

    Task LogRefund(CancellationToken ct)
    {
        logger.LogWarning("Refunding order {OrderId}", _order.OrderId);
        return Task.CompletedTask;
    }

    [FlowStep(Order = 5)]
    async Task ConfirmOrder(CancellationToken ct)
    {
        _order.Status = OrderStatus.Confirmed;
        await orderRepository.UpdateAsync(_order, ct);
        await mediator.PublishAsync(new OrderCreatedEvent(
            _order.OrderId, _order.CustomerId, _order.Items, _order.TotalAmount, _order.CreatedAt), ct);
    }
}
