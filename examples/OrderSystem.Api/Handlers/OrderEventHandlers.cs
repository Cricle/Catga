using Catga.Handlers;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Order created notification handler - sends notifications
/// </summary>
public class OrderCreatedNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedNotificationHandler> _logger;

    public OrderCreatedNotificationHandler(ILogger<OrderCreatedNotificationHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Send notifications (email, SMS, etc.)
        _logger.LogInformation("Notification sent: order {OrderId}, customer {CustomerId}",
            @event.OrderId, @event.CustomerId);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Order paid shipping handler - triggers shipping workflow
/// </summary>
public class OrderPaidShippingHandler : IEventHandler<OrderPaidEvent>
{
    private readonly ILogger<OrderPaidShippingHandler> _logger;

    public OrderPaidShippingHandler(ILogger<OrderPaidShippingHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderPaidEvent @event, CancellationToken cancellationToken = default)
    {
        // Trigger shipping workflow
        _logger.LogInformation("Shipping triggered: order {OrderId}, amount {Amount}",
            @event.OrderId, @event.Amount);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Order cancelled refund handler - processes refunds
/// </summary>
public class OrderCancelledRefundHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly ILogger<OrderCancelledRefundHandler> _logger;

    public OrderCancelledRefundHandler(ILogger<OrderCancelledRefundHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderCancelledEvent @event, CancellationToken cancellationToken = default)
    {
        // Process refund
        _logger.LogInformation("Refund processed: order {OrderId}, reason: {Reason}",
            @event.OrderId, @event.Reason);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Inventory reserved log handler - logs inventory operations
/// </summary>
public class InventoryReservedLogHandler : IEventHandler<InventoryReservedEvent>
{
    private readonly ILogger<InventoryReservedLogHandler> _logger;

    public InventoryReservedLogHandler(ILogger<InventoryReservedLogHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(InventoryReservedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Inventory reserved: order {OrderId}, items {Count}",
            @event.OrderId, @event.Items.Count);

        return Task.CompletedTask;
    }
}

