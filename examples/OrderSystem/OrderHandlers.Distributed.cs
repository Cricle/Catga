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

/// <summary>
/// 步骤1：预留库存
/// 成功后发布 InventoryReservedEvent 触发支付
/// 失败后发布 InventoryReservationFailedEvent 触发订单取消
/// </summary>
public class ReserveInventoryHandler : IRequestHandler<ReserveInventoryCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<ReserveInventoryHandler> _logger;

    public ReserveInventoryHandler(OrderDbContext db, ICatgaMediator mediator, ILogger<ReserveInventoryHandler> logger)
    {
        _db = db;
        _mediator = mediator;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<bool>> HandleAsync(
        ReserveInventoryCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤1] 预留库存 [OrderId={OrderId}, CorrelationId={CorrelationId}]",
            request.OrderId, request.CorrelationId);

        try
        {
            // 模拟库存检查（实际应该调用库存服务）
            var totalQuantity = request.Items.Sum(i => i.Quantity);
            if (totalQuantity > 100) // 模拟库存不足
            {
                throw new InvalidOperationException($"库存不足：需要 {totalQuantity} 件");
            }

            // 模拟预留库存
            var reservationId = $"RES-{Guid.NewGuid():N}";

            // 更新订单状态
            var order = await _db.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
            if (order != null)
            {
                order.ReservationId = reservationId;
                order.Status = OrderStatus.Processing;
                await _db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("✅ 库存已预留 [OrderId={OrderId}, ReservationId={ReservationId}]",
                request.OrderId, reservationId);

            // 成功 - 发布事件触发下一步（支付处理）
            await _mediator.PublishAsync(new InventoryReservedEvent(request.OrderId, reservationId)
            {
                CorrelationId = request.CorrelationId
            }, cancellationToken);

            return CatgaResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 库存预留失败 [OrderId={OrderId}]", request.OrderId);

            // 失败 - 发布失败事件触发补偿（取消订单）
            await _mediator.PublishAsync(new InventoryReservationFailedEvent(request.OrderId, ex.Message)
            {
                CorrelationId = request.CorrelationId
            }, cancellationToken);

            return CatgaResult<bool>.Failure(ex.Message);
        }
    }
}

/// <summary>
/// 步骤2：处理支付
/// 成功后发布 PaymentProcessedEvent 触发发货
/// 失败后发布 PaymentFailedEvent 触发库存释放
/// </summary>
public class ProcessPaymentHandler : IRequestHandler<ProcessPaymentCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<ProcessPaymentHandler> _logger;

    public ProcessPaymentHandler(OrderDbContext db, ICatgaMediator mediator, ILogger<ProcessPaymentHandler> logger)
    {
        _db = db;
        _mediator = mediator;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<bool>> HandleAsync(
        ProcessPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤2] 处理支付 [OrderId={OrderId}, Amount={Amount}, CorrelationId={CorrelationId}]",
            request.OrderId, request.Amount, request.CorrelationId);

        try
        {
            // 模拟支付处理（实际应该调用支付服务）
            if (request.Amount <= 0)
            {
                throw new InvalidOperationException("支付金额无效");
            }

            // 模拟支付
            var paymentId = $"PAY-{Guid.NewGuid():N}";

            // 更新订单状态
            var order = await _db.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
            if (order != null)
            {
                order.PaymentId = paymentId;
                await _db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("✅ 支付已处理 [OrderId={OrderId}, PaymentId={PaymentId}]",
                request.OrderId, paymentId);

            // 成功 - 发布事件触发下一步（创建发货）
            await _mediator.PublishAsync(new PaymentProcessedEvent(request.OrderId, paymentId)
            {
                CorrelationId = request.CorrelationId
            }, cancellationToken);

            return CatgaResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 支付处理失败 [OrderId={OrderId}]", request.OrderId);

            // 获取 ReservationId 用于补偿
            var order = await _db.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
            var reservationId = order?.ReservationId;

            // 失败 - 发布失败事件触发补偿（释放库存）
            await _mediator.PublishAsync(new PaymentFailedEvent(request.OrderId, ex.Message, reservationId)
            {
                CorrelationId = request.CorrelationId
            }, cancellationToken);

            return CatgaResult<bool>.Failure(ex.Message);
        }
    }
}

/// <summary>
/// 步骤3：创建发货
/// 成功后发布 ShipmentCreatedEvent 完成订单
/// 失败后发布 ShipmentFailedEvent 触发支付退款
/// </summary>
public class CreateShipmentHandler : IRequestHandler<CreateShipmentCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<CreateShipmentHandler> _logger;

    public CreateShipmentHandler(OrderDbContext db, ICatgaMediator mediator, ILogger<CreateShipmentHandler> logger)
    {
        _db = db;
        _mediator = mediator;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<bool>> HandleAsync(
        CreateShipmentCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤3] 创建发货 [OrderId={OrderId}, CorrelationId={CorrelationId}]",
            request.OrderId, request.CorrelationId);

        try
        {
            // 模拟创建发货（实际应该调用物流服务）
            var shipmentId = $"SHIP-{Guid.NewGuid():N}";

            // 更新订单状态
            var order = await _db.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
            if (order != null)
            {
                order.ShipmentId = shipmentId;
                order.Status = OrderStatus.Completed;
                order.CompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation("✅ 发货已创建 [OrderId={OrderId}, ShipmentId={ShipmentId}]",
                request.OrderId, shipmentId);

            // 成功 - 发布事件完成订单
            await _mediator.PublishAsync(new ShipmentCreatedEvent(request.OrderId, shipmentId)
            {
                CorrelationId = request.CorrelationId
            }, cancellationToken);

            return CatgaResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 发货创建失败 [OrderId={OrderId}]", request.OrderId);

            // 获取 PaymentId 和 ReservationId 用于补偿
            var order = await _db.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
            var paymentId = order?.PaymentId;
            var reservationId = order?.ReservationId;

            // 失败 - 发布失败事件触发补偿（退款）
            await _mediator.PublishAsync(new ShipmentFailedEvent(request.OrderId, ex.Message, paymentId, reservationId)
            {
                CorrelationId = request.CorrelationId
            }, cancellationToken);

            return CatgaResult<bool>.Failure(ex.Message);
        }
    }
}

#endregion

#region 补偿流程 Handlers

/// <summary>
/// 补偿：释放库存
/// 由 PaymentFailedEvent 或 ShipmentFailedEvent 触发
/// </summary>
public class ReleaseInventoryHandler : IRequestHandler<ReleaseInventoryCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ILogger<ReleaseInventoryHandler> _logger;

    public ReleaseInventoryHandler(OrderDbContext db, ILogger<ReleaseInventoryHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<bool>> HandleAsync(
        ReleaseInventoryCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[补偿] 释放库存 [OrderId={OrderId}, ReservationId={ReservationId}, CorrelationId={CorrelationId}]",
            request.OrderId, request.ReservationId, request.CorrelationId);

        // 模拟释放库存
        await Task.Delay(100, cancellationToken); // 模拟 API 调用

        // 清除订单的预留ID
        var order = await _db.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
        if (order != null)
        {
            order.ReservationId = null;
            await _db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("🔄 库存已释放 [OrderId={OrderId}]", request.OrderId);

        return CatgaResult<bool>.Success(true);
    }
}

/// <summary>
/// 补偿：退款
/// 由 ShipmentFailedEvent 触发
/// </summary>
public class RefundPaymentHandler : IRequestHandler<RefundPaymentCommand, bool>
{
    private readonly OrderDbContext _db;
    private readonly ILogger<RefundPaymentHandler> _logger;

    public RefundPaymentHandler(OrderDbContext db, ILogger<RefundPaymentHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<bool>> HandleAsync(
        RefundPaymentCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[补偿] 退款 [OrderId={OrderId}, PaymentId={PaymentId}, CorrelationId={CorrelationId}]",
            request.OrderId, request.PaymentId, request.CorrelationId);

        // 模拟退款
        await Task.Delay(100, cancellationToken); // 模拟 API 调用

        // 清除订单的支付ID
        var order = await _db.Orders.FindAsync(new object[] { request.OrderId }, cancellationToken);
        if (order != null)
        {
            order.PaymentId = null;
            await _db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("🔄 支付已退款 [OrderId={OrderId}]", request.OrderId);

        return CatgaResult<bool>.Success(true);
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

