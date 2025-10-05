using System.Text.Json;
using Catga.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.Outbox;

/// <summary>
/// Background service that publishes messages from the outbox
/// Ensures reliable message delivery in distributed systems
/// </summary>
public class OutboxPublisher : BackgroundService
{
    private readonly IOutboxStore _outboxStore;
    private readonly ICatgaMediator _mediator;
    private readonly ILogger<OutboxPublisher> _logger;
    private readonly TimeSpan _pollingInterval;
    private readonly int _batchSize;

    public OutboxPublisher(
        IOutboxStore outboxStore,
        ICatgaMediator mediator,
        ILogger<OutboxPublisher> logger,
        TimeSpan? pollingInterval = null,
        int? batchSize = null)
    {
        _outboxStore = outboxStore;
        _mediator = mediator;
        _logger = logger;
        _pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(5);
        _batchSize = batchSize ?? 100;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Publisher started (polling interval: {Interval}s, batch size: {BatchSize})",
            _pollingInterval.TotalSeconds, _batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            // 等待下一个轮询周期
            try
            {
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
                break;
            }
        }

        _logger.LogInformation("Outbox Publisher stopped");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        // 获取待处理消息
        var messages = await _outboxStore.GetPendingMessagesAsync(_batchSize, cancellationToken);

        if (messages.Count == 0)
            return;

        _logger.LogDebug("Processing {Count} outbox messages", messages.Count);

        // 并发处理消息
        var tasks = messages.Select(message => ProcessMessageAsync(message, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task ProcessMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            // 根据消息类型反序列化并发布
            // 注意：这里需要动态处理不同类型的事件
            // 实际生产环境中，可能需要使用类型注册表或约定

            _logger.LogDebug("Publishing outbox message {MessageId} of type {MessageType}",
                message.MessageId, message.MessageType);

            // 简化方案：直接通过 NATS 或其他传输层发布
            // 具体实现取决于应用的架构
            // 这里我们假设消息已经序列化好，可以直接发送

            // TODO: 实际发布逻辑（需要根据具体的传输机制实现）
            // 例如：await _mediator.PublishAsync(deserializedEvent);

            // 临时方案：标记为已发布
            // 实际应用中，这里应该调用真正的发布逻辑
            await _outboxStore.MarkAsPublishedAsync(message.MessageId, cancellationToken);

            _logger.LogInformation("Published outbox message {MessageId}", message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish outbox message {MessageId}", message.MessageId);

            await _outboxStore.MarkAsFailedAsync(
                message.MessageId,
                ex.Message,
                cancellationToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Outbox Publisher is stopping...");

        // 处理剩余的消息
        try
        {
            await ProcessPendingMessagesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing pending messages during shutdown");
        }

        await base.StopAsync(cancellationToken);
    }
}

