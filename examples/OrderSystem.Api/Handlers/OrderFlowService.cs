using Catga.Flow;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Order creation flow using [FlowStep] attributes.
/// Source Generator creates ExecuteFlowAsync(order, ct) automatically.
/// </summary>
public partial class OrderFlowService(
    IOrderRepository orderRepository,
    IInventoryService inventoryService,
    IPaymentService paymentService)
{
    [FlowStep(Order = 1)]
    private async Task CheckInventory(Order order, CancellationToken ct)
    {
        var result = await inventoryService.CheckStockAsync(order.Items);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
    }

    [FlowStep(Order = 2, Compensate = nameof(MarkOrderFailed))]
    private async Task SaveOrder(Order order, CancellationToken ct)
        => await orderRepository.SaveAsync(order);

    private async Task MarkOrderFailed(Order order, CancellationToken ct)
    {
        order.Status = OrderStatus.Failed;
        await orderRepository.UpdateAsync(order);
    }

    [FlowStep(Order = 3, Compensate = nameof(ReleaseInventory))]
    private async Task ReserveInventory(Order order, CancellationToken ct)
    {
        var result = await inventoryService.ReserveStockAsync(order.OrderId, order.Items);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
    }

    private async Task ReleaseInventory(Order order, CancellationToken ct)
        => await inventoryService.ReleaseStockAsync(order.OrderId, order.Items);

    [FlowStep(Order = 4, Compensate = nameof(RefundPayment))]
    private async Task ProcessPayment(Order order, CancellationToken ct)
    {
        var result = await paymentService.ProcessPaymentAsync(
            order.OrderId, order.TotalAmount, order.PaymentMethod);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
    }

    private Task RefundPayment(Order order, CancellationToken ct)
        => Task.CompletedTask;

    [FlowStep(Order = 5)]
    private async Task ConfirmOrder(Order order, CancellationToken ct)
    {
        order.Status = OrderStatus.Confirmed;
        order.UpdatedAt = DateTime.UtcNow;
        await orderRepository.UpdateAsync(order);
    }
}
