using Catga.Handlers;
using Catga.Results;
using Microsoft.Extensions.Logging;
using NotificationService.Events;

namespace NotificationService.Handlers;

/// <summary>
/// 订单创建通知处理器 - 发送邮件/短信通知
/// </summary>
public class OrderCreatedNotificationHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedNotificationHandler> _logger;

    public OrderCreatedNotificationHandler(ILogger<OrderCreatedNotificationHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("处理订单创建通知事件: {@Event}", @event);

            // 模拟发送邮件通知
            await SendEmailNotificationAsync(@event, cancellationToken);

            // 模拟发送短信通知
            await SendSmsNotificationAsync(@event, cancellationToken);

            _logger.LogInformation("订单创建通知发送成功: {OrderId}", @event.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送订单创建通知失败: {OrderId}", @event.OrderId);
            throw;
        }
    }

    private async Task SendEmailNotificationAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        // 模拟发送邮件的延迟
        await Task.Delay(500, cancellationToken);

        _logger.LogInformation("📧 邮件通知已发送 - 订单: {OrderId}, 客户: {CustomerId}, 产品: {ProductName}, 数量: {Quantity}, 总额: ¥{TotalAmount}",
            @event.OrderId, @event.CustomerId, @event.ProductName, @event.Quantity, @event.TotalAmount);
    }

    private async Task SendSmsNotificationAsync(OrderCreatedEvent @event, CancellationToken cancellationToken)
    {
        // 模拟发送短信的延迟
        await Task.Delay(300, cancellationToken);

        _logger.LogInformation("📱 短信通知已发送 - 订单: {OrderId}, 客户: {CustomerId}, 总额: ¥{TotalAmount}",
            @event.OrderId, @event.CustomerId, @event.TotalAmount);
    }
}

/// <summary>
/// 订单创建日志处理器 - 记录订单创建日志用于审计
/// </summary>
public class OrderCreatedLogHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly ILogger<OrderCreatedLogHandler> _logger;

    public OrderCreatedLogHandler(ILogger<OrderCreatedLogHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 模拟写入审计日志的操作
            await Task.Delay(100, cancellationToken);

            _logger.LogInformation("📊 审计日志记录 - 新订单创建: {@AuditLog}", new
            {
                Action = "OrderCreated",
                @event.OrderId,
                @event.CustomerId,
                @event.ProductId,
                @event.ProductName,
                @event.Quantity,
                @event.TotalAmount,
                Timestamp = @event.OccurredAt,
                Source = "OrderService"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "记录订单审计日志失败: {OrderId}", @event.OrderId);
            throw;
        }
    }
}
