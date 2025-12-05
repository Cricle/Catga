using Catga.Abstractions;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Sends notification when order is created.
/// </summary>
public partial class OrderCreatedNotificationHandler(
    ILogger<OrderCreatedNotificationHandler> logger) : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        LogNotificationSent(logger, @event.OrderId, @event.CustomerId, @event.TotalAmount);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification sent: order {OrderId}, customer {CustomerId}, amount {Amount}")]
    static partial void LogNotificationSent(ILogger logger, string orderId, string customerId, decimal amount);
}

/// <summary>
/// Updates analytics when order is created.
/// </summary>
public partial class OrderCreatedAnalyticsHandler(
    ILogger<OrderCreatedAnalyticsHandler> logger) : IEventHandler<OrderCreatedEvent>
{
    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        LogAnalyticsUpdated(logger, @event.OrderId, @event.Items.Count, @event.TotalAmount);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Analytics updated: order {OrderId}, items {Count}, total {Amount}")]
    static partial void LogAnalyticsUpdated(ILogger logger, string orderId, int count, decimal amount);
}

/// <summary>
/// Handles order cancellation events.
/// </summary>
public partial class OrderCancelledHandler(
    ILogger<OrderCancelledHandler> logger) : IEventHandler<OrderCancelledEvent>
{
    public Task HandleAsync(OrderCancelledEvent @event, CancellationToken cancellationToken = default)
    {
        LogOrderCancelled(logger, @event.OrderId, @event.Reason);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order cancelled: {OrderId}, reason: {Reason}")]
    static partial void LogOrderCancelled(ILogger logger, string orderId, string reason);
}

/// <summary>
/// Handles order failure events.
/// </summary>
public partial class OrderFailedHandler(
    ILogger<OrderFailedHandler> logger) : IEventHandler<OrderFailedEvent>
{
    public Task HandleAsync(OrderFailedEvent @event, CancellationToken cancellationToken = default)
    {
        LogOrderFailed(logger, @event.OrderId, @event.CustomerId, @event.Reason);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order failed: {OrderId}, customer {CustomerId}, reason: {Reason}")]
    static partial void LogOrderFailed(ILogger logger, string orderId, string customerId, string reason);
}
