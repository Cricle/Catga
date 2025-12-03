using Catga.Flow;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Order creation flow using [FlowStep] attributes.
/// Source Generator creates ExecuteFlowAsync(order, ct) automatically.
///
/// Usage:
///   var service = new OrderFlowService(repo, inventory, payment);
///   var result = await service.ExecuteFlowAsync(order, ct);
/// </summary>
public partial class OrderFlowService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;

    public OrderFlowService(
        IOrderRepository orderRepository,
        IInventoryService inventoryService,
        IPaymentService paymentService)
    {
        _orderRepository = orderRepository;
        _inventoryService = inventoryService;
        _paymentService = paymentService;
    }

    [FlowStep(Order = 1)]
    private async Task CheckInventory(Order order)
    {
        var result = await _inventoryService.CheckStockAsync(order.Items);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
    }

    [FlowStep(Order = 2, Compensate = nameof(MarkOrderFailed))]
    private async Task SaveOrder(Order order)
    {
        await _orderRepository.SaveAsync(order);
    }

    private async Task MarkOrderFailed(Order order)
    {
        order.Status = OrderStatus.Failed;
        await _orderRepository.UpdateAsync(order);
    }

    [FlowStep(Order = 3, Compensate = nameof(ReleaseInventory))]
    private async Task ReserveInventory(Order order)
    {
        var result = await _inventoryService.ReserveStockAsync(order.OrderId, order.Items);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
    }

    private async Task ReleaseInventory(Order order)
    {
        await _inventoryService.ReleaseStockAsync(order.OrderId, order.Items);
    }

    [FlowStep(Order = 4, Compensate = nameof(RefundPayment))]
    private async Task ProcessPayment(Order order)
    {
        var result = await _paymentService.ProcessPaymentAsync(
            order.OrderId, order.TotalAmount, order.PaymentMethod);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
    }

    private Task RefundPayment(Order order)
    {
        return Task.CompletedTask;
    }

    [FlowStep(Order = 5)]
    private async Task ConfirmOrder(Order order)
    {
        order.Status = OrderStatus.Confirmed;
        order.UpdatedAt = DateTime.UtcNow;
        await _orderRepository.UpdateAsync(order);
    }
}
