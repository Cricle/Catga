using Catga.Flow;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Services;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Order creation flow using FlowService base class.
/// Just override DefineSteps() and write your business logic.
///
/// Usage:
///   var flow = new OrderFlowService(repo, inventory, payment, order);
///   var result = await flow.ExecuteAsync();
/// </summary>
public class OrderFlowService : FlowService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentService _paymentService;
    private readonly Order _order;

    public OrderFlowService(
        IOrderRepository orderRepository,
        IInventoryService inventoryService,
        IPaymentService paymentService,
        Order order)
    {
        _orderRepository = orderRepository;
        _inventoryService = inventoryService;
        _paymentService = paymentService;
        _order = order;
    }

    protected override void DefineSteps()
    {
        // Step 1: Check inventory (no compensation - read only)
        Step(CheckInventory);

        // Step 2: Save order (with compensation)
        Step(SaveOrder, MarkOrderFailed);

        // Step 3: Reserve inventory (with compensation)
        Step(ReserveInventory, ReleaseInventory);

        // Step 4: Process payment (with compensation)
        Step(ProcessPayment, RefundPayment);

        // Step 5: Confirm order (final step)
        Step(ConfirmOrder);
    }

    private async Task CheckInventory()
    {
        var result = await _inventoryService.CheckStockAsync(_order.Items);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
    }

    private async Task SaveOrder()
    {
        await _orderRepository.SaveAsync(_order);
    }

    private async Task MarkOrderFailed()
    {
        _order.Status = OrderStatus.Failed;
        await _orderRepository.UpdateAsync(_order);
    }

    private async Task ReserveInventory()
    {
        var result = await _inventoryService.ReserveStockAsync(_order.OrderId, _order.Items);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
    }

    private async Task ReleaseInventory()
    {
        await _inventoryService.ReleaseStockAsync(_order.OrderId, _order.Items);
    }

    private async Task ProcessPayment()
    {
        var result = await _paymentService.ProcessPaymentAsync(
            _order.OrderId, _order.TotalAmount, _order.PaymentMethod);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
    }

    private Task RefundPayment()
    {
        // In real implementation, call payment gateway refund API
        return Task.CompletedTask;
    }

    private async Task ConfirmOrder()
    {
        _order.Status = OrderStatus.Confirmed;
        _order.UpdatedAt = DateTime.UtcNow;
        await _orderRepository.UpdateAsync(_order);
    }
}
