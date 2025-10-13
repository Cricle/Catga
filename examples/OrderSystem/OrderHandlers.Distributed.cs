using Catga;
using Catga.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace OrderSystem;

// ==========================================
// 分布式长事务 Handler 实现
// ==========================================
// 展示 Catga 的零编排、自动补偿能力
// 通过事件链自动触发下一步，失败自动补偿
// ==========================================

#region 正向流程 Handlers

/// <summary>
/// 步骤0：创建订单
/// 成功后发布 OrderCreatedEvent 自动触发库存预留
/// </summary>
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    private readonly OrderDbContext _db;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateOrderHandler> _logger;

    public CreateOrderHandler(OrderDbContext db, ICatgaMediator mediator, ILogger<CreateOrderHandler> logger)
    {
        _db = db;
        _mediator = mediator;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<CreateOrderResult>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[事务开始] 创建订单 [CorrelationId={CorrelationId}]", request.CorrelationId);

        // 1. 创建订单
        var order = new Order
        {
            OrderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
            CustomerName = request.CustomerName,
            TotalAmount = request.Items.Sum(i => i.Price * i.Quantity),
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("✅ 订单已创建 [OrderId={OrderId}, OrderNumber={OrderNumber}]",
            order.Id, order.OrderNumber);

        // 2. 发布事件 - 自动触发下一步（库存预留）
        await _mediator.PublishAsync(new OrderCreatedEvent(
            order.Id,
            order.OrderNumber,
            order.CustomerName,
            order.TotalAmount,
            request.Items)
        {
            CorrelationId = request.CorrelationId
        }, cancellationToken);

        return CatgaResult<CreateOrderResult>.Success(
            new CreateOrderResult(order.Id, order.OrderNumber));
    }
}

/// <summary>步骤1：预留库存</summary>
public class ReserveInventoryHandler : IRequestHandler<ReserveInventoryCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<ReserveInventoryHandler> _logger;

    public ReserveInventoryHandler(OrderDbContext db, ICatgaMediator mediator, ILogger<ReserveInventoryHandler> logger)
        => (_db, _mediator, _logger) = (db, mediator, logger);

    public async ValueTask<CatgaResult<bool>> HandleAsync(ReserveInventoryCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤1] 预留库存 [OrderId={OrderId}, CorrelationId={CorrelationId}]",
            request.OrderId, request.CorrelationId);

        try
        {
            if (request.Items.Sum(i => i.Quantity) > 100)
                throw new InvalidOperationException($"库存不足：需要 {request.Items.Sum(i => i.Quantity)} 件");

            var reservationId = $"RES-{Guid.NewGuid():N}";
            await UpdateOrderFieldAsync(request.OrderId, o => { o.ReservationId = reservationId; o.Status = OrderStatus.Processing; }, cancellationToken);

            _logger.LogInformation("✅ 库存已预留 [OrderId={OrderId}, ReservationId={ReservationId}]", request.OrderId, reservationId);
            await _mediator.PublishAsync(new InventoryReservedEvent(request.OrderId, reservationId) { CorrelationId = request.CorrelationId }, cancellationToken);
            return CatgaResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 库存预留失败 [OrderId={OrderId}]", request.OrderId);
            await _mediator.PublishAsync(new InventoryReservationFailedEvent(request.OrderId, ex.Message) { CorrelationId = request.CorrelationId }, cancellationToken);
            return CatgaResult<bool>.Failure(ex.Message);
        }
    }

    private async Task UpdateOrderFieldAsync(long orderId, Action<Order> update, CancellationToken ct)
    {
        var order = await _db.Orders.FindAsync(new object[] { orderId }, ct);
        if (order != null) { update(order); await _db.SaveChangesAsync(ct); }
    }
}

/// <summary>步骤2：处理支付</summary>
public class ProcessPaymentHandler : IRequestHandler<ProcessPaymentCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<ProcessPaymentHandler> _logger;

    public ProcessPaymentHandler(OrderDbContext db, ICatgaMediator mediator, ILogger<ProcessPaymentHandler> logger)
        => (_db, _mediator, _logger) = (db, mediator, logger);

    public async ValueTask<CatgaResult<bool>> HandleAsync(ProcessPaymentCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤2] 处理支付 [OrderId={OrderId}, Amount={Amount}, CorrelationId={CorrelationId}]",
            request.OrderId, request.Amount, request.CorrelationId);

        try
        {
            if (request.Amount <= 0) throw new InvalidOperationException("支付金额无效");

            var paymentId = $"PAY-{Guid.NewGuid():N}";
            await UpdateOrderFieldAsync(request.OrderId, o => o.PaymentId = paymentId, cancellationToken);

            _logger.LogInformation("✅ 支付已处理 [OrderId={OrderId}, PaymentId={PaymentId}]", request.OrderId, paymentId);
            await _mediator.PublishAsync(new PaymentProcessedEvent(request.OrderId, paymentId) { CorrelationId = request.CorrelationId }, cancellationToken);
            return CatgaResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 支付处理失败 [OrderId={OrderId}]", request.OrderId);
            var reservationId = (await _db.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken))?.ReservationId;
            await _mediator.PublishAsync(new PaymentFailedEvent(request.OrderId, ex.Message, reservationId) { CorrelationId = request.CorrelationId }, cancellationToken);
            return CatgaResult<bool>.Failure(ex.Message);
        }
    }

    private async Task UpdateOrderFieldAsync(long orderId, Action<Order> update, CancellationToken ct)
    {
        var order = await _db.Orders.FindAsync(new object[] { orderId }, ct);
        if (order != null) { update(order); await _db.SaveChangesAsync(ct); }
    }
}

/// <summary>步骤3：创建发货</summary>
public class CreateShipmentHandler : IRequestHandler<CreateShipmentCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateShipmentHandler> _logger;

    public CreateShipmentHandler(OrderDbContext db, ICatgaMediator mediator, ILogger<CreateShipmentHandler> logger)
        => (_db, _mediator, _logger) = (db, mediator, logger);

    public async ValueTask<CatgaResult<bool>> HandleAsync(CreateShipmentCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤3] 创建发货 [OrderId={OrderId}, CorrelationId={CorrelationId}]",
            request.OrderId, request.CorrelationId);

        try
        {
            var shipmentId = $"SHIP-{Guid.NewGuid():N}";
            await UpdateOrderFieldAsync(request.OrderId, o => 
            { 
                o.ShipmentId = shipmentId; 
                o.Status = OrderStatus.Completed; 
                o.CompletedAt = DateTime.UtcNow; 
            }, cancellationToken);

            _logger.LogInformation("✅ 发货已创建 [OrderId={OrderId}, ShipmentId={ShipmentId}]", request.OrderId, shipmentId);
            await _mediator.PublishAsync(new ShipmentCreatedEvent(request.OrderId, shipmentId) { CorrelationId = request.CorrelationId }, cancellationToken);
            return CatgaResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 发货创建失败 [OrderId={OrderId}]", request.OrderId);
            var order = await _db.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
            await _mediator.PublishAsync(new ShipmentFailedEvent(request.OrderId, ex.Message, order?.PaymentId, order?.ReservationId) 
                { CorrelationId = request.CorrelationId }, cancellationToken);
            return CatgaResult<bool>.Failure(ex.Message);
        }
    }

    private async Task UpdateOrderFieldAsync(long orderId, Action<Order> update, CancellationToken ct)
    {
        var order = await _db.Orders.FindAsync(new object[] { orderId }, ct);
        if (order != null) { update(order); await _db.SaveChangesAsync(ct); }
    }
}

#endregion

#region 补偿流程 Handlers

/// <summary>补偿：释放库存</summary>
public class ReleaseInventoryHandler : IRequestHandler<ReleaseInventoryCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ILogger<ReleaseInventoryHandler> _logger;

    public ReleaseInventoryHandler(OrderDbContext db, ILogger<ReleaseInventoryHandler> logger)
        => (_db, _logger) = (db, logger);

    public async ValueTask<CatgaResult<bool>> HandleAsync(ReleaseInventoryCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[补偿] 释放库存 [OrderId={OrderId}, ReservationId={ReservationId}, CorrelationId={CorrelationId}]",
            request.OrderId, request.ReservationId, request.CorrelationId);

        await Task.Delay(100, cancellationToken);
        await UpdateOrderFieldAsync(request.OrderId, o => o.ReservationId = null, cancellationToken);
        _logger.LogInformation("🔄 库存已释放 [OrderId={OrderId}]", request.OrderId);
        return CatgaResult<bool>.Success(true);
    }

    private async Task UpdateOrderFieldAsync(long orderId, Action<Order> update, CancellationToken ct)
    {
        var order = await _db.Orders.FindAsync(new object[] { orderId }, ct);
        if (order != null) { update(order); await _db.SaveChangesAsync(ct); }
    }
}

/// <summary>补偿：退款</summary>
public class RefundPaymentHandler : IRequestHandler<RefundPaymentCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ILogger<RefundPaymentHandler> _logger;

    public RefundPaymentHandler(OrderDbContext db, ILogger<RefundPaymentHandler> logger)
        => (_db, _logger) = (db, logger);

    public async ValueTask<CatgaResult<bool>> HandleAsync(RefundPaymentCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[补偿] 退款 [OrderId={OrderId}, PaymentId={PaymentId}, CorrelationId={CorrelationId}]",
            request.OrderId, request.PaymentId, request.CorrelationId);

        await Task.Delay(100, cancellationToken);
        await UpdateOrderFieldAsync(request.OrderId, o => o.PaymentId = null, cancellationToken);
        _logger.LogInformation("🔄 支付已退款 [OrderId={OrderId}]", request.OrderId);
        return CatgaResult<bool>.Success(true);
    }

    private async Task UpdateOrderFieldAsync(long orderId, Action<Order> update, CancellationToken ct)
    {
        var order = await _db.Orders.FindAsync(new object[] { orderId }, ct);
        if (order != null) { update(order); await _db.SaveChangesAsync(ct); }
    }
}

/// <summary>
/// 补偿：取消订单
/// 最终的补偿操作
/// </summary>
public class CancelOrderHandler : IRequestHandler<CancelOrderCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CancelOrderHandler> _logger;

    public CancelOrderHandler(OrderDbContext db, ICatgaMediator mediator, ILogger<CancelOrderHandler> logger)
    {
        _db = db;
        _mediator = mediator;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<bool>> HandleAsync(
        CancelOrderCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[补偿] 取消订单 [OrderId={OrderId}, Reason={Reason}, CorrelationId={CorrelationId}]",
            request.OrderId, request.Reason, request.CorrelationId);

        var order = await _db.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
        if (order != null)
        {
            order.Status = OrderStatus.Cancelled;
            order.CancellationReason = request.Reason;
            await _db.SaveChangesAsync(cancellationToken);

            // 发布取消事件
            await _mediator.PublishAsync(new OrderCancelledEvent(order.Id, order.OrderNumber, request.Reason)
            {
                CorrelationId = request.CorrelationId
            }, cancellationToken);

            _logger.LogInformation("🔄 订单已取消 [OrderId={OrderId}, OrderNumber={OrderNumber}]",
                order.Id, order.OrderNumber);
        }

        return CatgaResult<bool>.Success(true);
    }
}

#endregion

