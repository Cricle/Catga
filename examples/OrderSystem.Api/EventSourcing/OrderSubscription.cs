using Catga.Abstractions;
using Catga.EventSourcing;
using Microsoft.Extensions.Logging;
using OrderSystem.Api.Domain;

namespace OrderSystem.Api.EventSourcing;

/// <summary>
/// Order event handler for subscriptions.
/// Demonstrates the subscription feature.
/// </summary>
public class OrderEventSubscriptionHandler : IEventHandler
{
    private readonly ILogger<OrderEventSubscriptionHandler> _logger;

    public OrderEventSubscriptionHandler(ILogger<OrderEventSubscriptionHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(IEvent @event, CancellationToken ct = default)
    {
        switch (@event)
        {
            case OrderAggregateCreated e:
                _logger.LogInformation("[Subscription] Order created: {OrderId} for customer {CustomerId}",
                    e.OrderId, e.CustomerId);
                break;

            case OrderItemAdded e:
                _logger.LogInformation("[Subscription] Item added to order {OrderId}: {ProductName} x{Quantity}",
                    e.OrderId, e.ProductName, e.Quantity);
                break;

            case OrderStatusChanged e:
                _logger.LogInformation("[Subscription] Order {OrderId} status changed to: {Status}",
                    e.OrderId, e.NewStatus);
                // Could trigger notifications, webhooks, etc.
                break;

            case OrderDiscountApplied e:
                _logger.LogInformation("[Subscription] Discount applied to order {OrderId}: -{DiscountAmount:C}",
                    e.OrderId, e.DiscountAmount);
                break;
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Notification handler for order events.
/// </summary>
public class OrderNotificationHandler : IEventHandler
{
    private readonly ILogger<OrderNotificationHandler> _logger;

    public OrderNotificationHandler(ILogger<OrderNotificationHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(IEvent @event, CancellationToken ct = default)
    {
        if (@event is OrderStatusChanged e && e.NewStatus == "Confirmed")
        {
            _logger.LogInformation("[Notification] Sending confirmation email for order {OrderId}", e.OrderId);
            // Send email, SMS, push notification, etc.
        }

        return ValueTask.CompletedTask;
    }
}
