using Catga.Abstractions;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Handlers;

/// <summary>Logs order creation events.</summary>
public class OrderCreatedEventHandler(
    ILogger<OrderCreatedEventHandler> logger) : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Order created: {OrderId}, Customer: {CustomerId}, Amount: {Amount:C}",
            @event.OrderId, @event.CustomerId, @event.TotalAmount);
        return Task.CompletedTask;
    }
}

/// <summary>Sends notification when order is created (simulated).</summary>
public class SendOrderNotificationHandler(
    ILogger<SendOrderNotificationHandler> logger) : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Notification sent for order {OrderId} to customer {CustomerId}",
            @event.OrderId, @event.CustomerId);
        return Task.CompletedTask;
    }
}

/// <summary>Logs order cancellation events.</summary>
public class OrderCancelledEventHandler(
    ILogger<OrderCancelledEventHandler> logger) : IEventHandler<OrderCancelledEvent>
{
    public Task HandleAsync(OrderCancelledEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Order cancelled: {OrderId}, Reason: {Reason}",
            @event.OrderId, @event.Reason ?? "Not specified");
        return Task.CompletedTask;
    }
}

/// <summary>Logs order confirmation events.</summary>
public class OrderConfirmedEventHandler(
    ILogger<OrderConfirmedEventHandler> logger) : IEventHandler<OrderConfirmedEvent>
{
    public Task HandleAsync(OrderConfirmedEvent @event, CancellationToken ct = default)
    {
        logger.LogInformation("Order confirmed: {OrderId}", @event.OrderId);
        return Task.CompletedTask;
    }
}
