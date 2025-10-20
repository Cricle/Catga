using Catga.Abstractions;
using OrderSystem.Api.Messages;
using Catga.Abstractions;

namespace OrderSystem.Api.Handlers;

public partial class OrderCreatedNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedNotificationHandler> _logger;

    public OrderCreatedNotificationHandler(ILogger<OrderCreatedNotificationHandler> logger) => _logger = logger;

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        LogNotificationSent(@event.OrderId, @event.CustomerId, @event.TotalAmount);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "📧 Notification sent: order {OrderId}, customer {CustomerId}, amount {Amount}")]
    partial void LogNotificationSent(string orderId, string customerId, decimal amount);
}

public partial class OrderCreatedAnalyticsHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedAnalyticsHandler> _logger;

    public OrderCreatedAnalyticsHandler(ILogger<OrderCreatedAnalyticsHandler> logger) => _logger = logger;

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        LogAnalyticsUpdated(@event.OrderId, @event.Items.Count, @event.TotalAmount);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "📊 Analytics updated: order {OrderId}, items {Count}, total {Amount}")]
    partial void LogAnalyticsUpdated(string orderId, int count, decimal amount);
}

public partial class OrderCancelledHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly ILogger<OrderCancelledHandler> _logger;

    public OrderCancelledHandler(ILogger<OrderCancelledHandler> logger) => _logger = logger;

    public Task HandleAsync(OrderCancelledEvent @event, CancellationToken cancellationToken = default)
    {
        LogOrderCancelled(@event.OrderId, @event.Reason);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "❌ Order cancelled: {OrderId}, reason: {Reason}")]
    partial void LogOrderCancelled(string orderId, string reason);
}

public partial class OrderFailedHandler : IEventHandler<OrderFailedEvent>
{
    private readonly ILogger<OrderFailedHandler> _logger;

    public OrderFailedHandler(ILogger<OrderFailedHandler> logger) => _logger = logger;

    public Task HandleAsync(OrderFailedEvent @event, CancellationToken cancellationToken = default)
    {
        LogOrderFailed(@event.OrderId, @event.CustomerId, @event.Reason);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "⚠️ Order failed: {OrderId}, customer {CustomerId}, reason: {Reason}")]
    partial void LogOrderFailed(string orderId, string customerId, string reason);
}
