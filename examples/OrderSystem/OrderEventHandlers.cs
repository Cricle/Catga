using Catga.Messages;
using Microsoft.Extensions.Logging;

namespace OrderSystem;

// ==========================================
// 事件处理器 - 实现零编排的自动触发
// ==========================================
// 通过事件链自动触发下一步
// 失败事件自动触发补偿
// 完全去中心化，无需编排器
// ==========================================

#region 正向流程事件处理器

/// <summary>订单已创建 → 自动触发库存预留</summary>
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(ICatgaMediator mediator, ILogger<OrderCreatedEventHandler> logger)
        => (_mediator, _logger) = (mediator, logger);

    public async Task HandleAsync(OrderCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[事件] 订单已创建，自动触发库存预留 [OrderId={OrderId}, CorrelationId={CorrelationId}]",
            @event.OrderId, @event.CorrelationId);
        await _mediator.SendAsync(new ReserveInventoryCommand(@event.OrderId, @event.Items)
            { CorrelationId = @event.CorrelationId }, cancellationToken);
    }
}

/// <summary>库存已预留 → 自动触发支付处理</summary>
public class InventoryReservedEventHandler : IEventHandler<InventoryReservedEvent>
{
    private readonly ICatgaMediator _mediator;
    private readonly OrderDbContext _db;
    private readonly ILogger<InventoryReservedEventHandler> _logger;

    public InventoryReservedEventHandler(ICatgaMediator mediator, OrderDbContext db, ILogger<InventoryReservedEventHandler> logger)
        => (_mediator, _db, _logger) = (mediator, db, logger);

    public async Task HandleAsync(InventoryReservedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[事件] 库存已预留，自动触发支付处理 [OrderId={OrderId}, CorrelationId={CorrelationId}]",
            @event.OrderId, @event.CorrelationId);

        var order = await _db.Orders.FindAsync(new object[] { @event.OrderId }, cancellationToken);
        if (order != null)
            await _mediator.SendAsync(new ProcessPaymentCommand(@event.OrderId, order.TotalAmount)
                { CorrelationId = @event.CorrelationId }, cancellationToken);
    }
}

/// <summary>支付已处理 → 自动触发发货创建</summary>
public class PaymentProcessedEventHandler : IEventHandler<PaymentProcessedEvent>
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<PaymentProcessedEventHandler> _logger;

    public PaymentProcessedEventHandler(ICatgaMediator mediator, ILogger<PaymentProcessedEventHandler> logger)
        => (_mediator, _logger) = (mediator, logger);

    public async Task HandleAsync(PaymentProcessedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[事件] 支付已处理，自动触发发货创建 [OrderId={OrderId}, CorrelationId={CorrelationId}]",
            @event.OrderId, @event.CorrelationId);
        await _mediator.SendAsync(new CreateShipmentCommand(@event.OrderId)
            { CorrelationId = @event.CorrelationId }, cancellationToken);
    }
}

/// <summary>发货已创建 → 订单完成</summary>
public class ShipmentCreatedEventHandler : IEventHandler<ShipmentCreatedEvent>
{
    private readonly ILogger<ShipmentCreatedEventHandler> _logger;
    public ShipmentCreatedEventHandler(ILogger<ShipmentCreatedEventHandler> logger) => _logger = logger;

    public Task HandleAsync(ShipmentCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🎉 [事件完成] 订单处理完成 [OrderId={OrderId}, CorrelationId={CorrelationId}]",
            @event.OrderId, @event.CorrelationId);
        return Task.CompletedTask;
    }
}

#endregion

#region 补偿流程事件处理器

/// <summary>库存预留失败 → 自动触发订单取消</summary>
public class InventoryReservationFailedEventHandler : IEventHandler<InventoryReservationFailedEvent>
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<InventoryReservationFailedEventHandler> _logger;

    public InventoryReservationFailedEventHandler(ICatgaMediator mediator, ILogger<InventoryReservationFailedEventHandler> logger)
        => (_mediator, _logger) = (mediator, logger);

    public async Task HandleAsync(InventoryReservationFailedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[补偿事件] 库存预留失败，自动触发订单取消 [OrderId={OrderId}, Reason={Reason}, CorrelationId={CorrelationId}]",
            @event.OrderId, @event.Reason, @event.CorrelationId);
        await _mediator.SendAsync(new CancelOrderCommand(@event.OrderId, $"库存预留失败: {@event.Reason}")
            { CorrelationId = @event.CorrelationId }, cancellationToken);
    }
}

/// <summary>支付失败 → 自动触发库存释放</summary>
public class PaymentFailedEventHandler : IEventHandler<PaymentFailedEvent>
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<PaymentFailedEventHandler> _logger;

    public PaymentFailedEventHandler(ICatgaMediator mediator, ILogger<PaymentFailedEventHandler> logger)
        => (_mediator, _logger) = (mediator, logger);

    public async Task HandleAsync(PaymentFailedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[补偿事件] 支付失败，自动触发库存释放 [OrderId={OrderId}, Reason={Reason}, CorrelationId={CorrelationId}]",
            @event.OrderId, @event.Reason, @event.CorrelationId);

        if (!string.IsNullOrEmpty(@event.ReservationId))
            await _mediator.SendAsync(new ReleaseInventoryCommand(@event.OrderId, @event.ReservationId)
                { CorrelationId = @event.CorrelationId }, cancellationToken);

        await _mediator.SendAsync(new CancelOrderCommand(@event.OrderId, $"支付失败: {@event.Reason}")
            { CorrelationId = @event.CorrelationId }, cancellationToken);
    }
}

/// <summary>发货失败 → 自动触发支付退款和库存释放</summary>
public class ShipmentFailedEventHandler : IEventHandler<ShipmentFailedEvent>
{
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<ShipmentFailedEventHandler> _logger;

    public ShipmentFailedEventHandler(ICatgaMediator mediator, ILogger<ShipmentFailedEventHandler> logger)
        => (_mediator, _logger) = (mediator, logger);

    public async Task HandleAsync(ShipmentFailedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[补偿事件] 发货失败，自动触发支付退款和库存释放 [OrderId={OrderId}, Reason={Reason}, CorrelationId={CorrelationId}]",
            @event.OrderId, @event.Reason, @event.CorrelationId);

        if (!string.IsNullOrEmpty(@event.PaymentId))
            await _mediator.SendAsync(new RefundPaymentCommand(@event.OrderId, @event.PaymentId)
                { CorrelationId = @event.CorrelationId }, cancellationToken);

        if (!string.IsNullOrEmpty(@event.ReservationId))
            await _mediator.SendAsync(new ReleaseInventoryCommand(@event.OrderId, @event.ReservationId)
                { CorrelationId = @event.CorrelationId }, cancellationToken);

        await _mediator.SendAsync(new CancelOrderCommand(@event.OrderId, $"发货失败: {@event.Reason}")
            { CorrelationId = @event.CorrelationId }, cancellationToken);
    }
}

/// <summary>订单已取消 → 记录日志</summary>
public class OrderCancelledEventHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly ILogger<OrderCancelledEventHandler> _logger;
    public OrderCancelledEventHandler(ILogger<OrderCancelledEventHandler> logger) => _logger = logger;

    public Task HandleAsync(OrderCancelledEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("🔄 [补偿完成] 订单已取消 [OrderId={OrderId}, OrderNumber={OrderNumber}, Reason={Reason}, CorrelationId={CorrelationId}]",
            @event.OrderId, @event.OrderNumber, @event.Reason, @event.CorrelationId);
        return Task.CompletedTask;
    }
}

#endregion

