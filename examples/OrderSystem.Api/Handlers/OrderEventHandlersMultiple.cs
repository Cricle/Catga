using Catga;
using Catga.Handlers;
using Catga.Messages;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Handlers;

/// <summary>
/// Multiple event handlers demonstration - one event can trigger multiple handlers
/// This showcases the power of event-driven architecture
/// </summary>

/// <summary>Handler 1: Send notification when order is created</summary>
public class SendOrderNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<SendOrderNotificationHandler> _logger;

    public SendOrderNotificationHandler(ILogger<SendOrderNotificationHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📧 Sending notification to customer {CustomerId} for order {OrderId}",
            @event.CustomerId, @event.OrderId);

        // Simulate email/SMS notification
        await Task.Delay(50, cancellationToken);

        _logger.LogInformation("✅ Notification sent successfully");
    }
}

/// <summary>Handler 2: Update analytics when order is created</summary>
public class UpdateAnalyticsHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<UpdateAnalyticsHandler> _logger;

    public UpdateAnalyticsHandler(ILogger<UpdateAnalyticsHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📊 Updating analytics for order {OrderId}, total: {Total:C}",
            @event.OrderId, @event.TotalAmount);

        // Simulate analytics update (e.g., Google Analytics, Application Insights)
        await Task.Delay(30, cancellationToken);

        _logger.LogInformation("✅ Analytics updated");
    }
}

/// <summary>Handler 3: Update inventory when order is paid</summary>
public class UpdateInventoryOnPaymentHandler : IEventHandler<OrderPaidEvent>
{
    private readonly ILogger<UpdateInventoryOnPaymentHandler> _logger;

    public UpdateInventoryOnPaymentHandler(ILogger<UpdateInventoryOnPaymentHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderPaidEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📦 Updating inventory for paid order {OrderId}",
            @event.OrderId);

        // Simulate inventory service call
        await Task.Delay(100, cancellationToken);

        _logger.LogInformation("✅ Inventory updated");
    }
}

/// <summary>Handler 4: Prepare shipment when order is paid</summary>
public class PrepareShipmentHandler : IEventHandler<OrderPaidEvent>
{
    private readonly ILogger<PrepareShipmentHandler> _logger;

    public PrepareShipmentHandler(ILogger<PrepareShipmentHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderPaidEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "🚚 Preparing shipment for order {OrderId}",
            @event.OrderId);

        // Simulate warehouse notification
        await Task.Delay(80, cancellationToken);

        _logger.LogInformation("✅ Shipment prepared");
    }
}

/// <summary>Handler 5: Record logistics tracking when order is shipped</summary>
public class RecordLogisticsHandler : IEventHandler<OrderShippedEvent>
{
    private readonly ILogger<RecordLogisticsHandler> _logger;

    public RecordLogisticsHandler(ILogger<RecordLogisticsHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderShippedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📍 Recording logistics for order {OrderId}, tracking: {TrackingNumber}",
            @event.OrderId, @event.TrackingNumber);

        // Simulate logistics system update
        await Task.Delay(60, cancellationToken);

        _logger.LogInformation("✅ Logistics recorded");
    }
}

/// <summary>Handler 6: Send shipment notification when order is shipped</summary>
public class SendShipmentNotificationHandler : IEventHandler<OrderShippedEvent>
{
    private readonly ILogger<SendShipmentNotificationHandler> _logger;

    public SendShipmentNotificationHandler(ILogger<SendShipmentNotificationHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(OrderShippedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "📧 Sending shipment notification for order {OrderId}",
            @event.OrderId);

        // Simulate notification service
        await Task.Delay(40, cancellationToken);

        _logger.LogInformation("✅ Shipment notification sent");
    }
}

