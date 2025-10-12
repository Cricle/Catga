using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.Outbox;

/// <summary>Background service that publishes messages from the outbox</summary>
public class OutboxPublisher : BackgroundService
{
    private readonly IOutboxStore _outboxStore;
    private readonly ILogger<OutboxPublisher> _logger;
    private readonly TimeSpan _pollingInterval;
    private readonly int _batchSize;

    public OutboxPublisher(IOutboxStore outboxStore, ILogger<OutboxPublisher> logger, TimeSpan? pollingInterval = null, int? batchSize = null)
    {
        _outboxStore = outboxStore;
        _logger = logger;
        _pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(5);
        _batchSize = batchSize ?? 100;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Publisher started (polling interval: {Interval}s, batch size: {BatchSize})", _pollingInterval.TotalSeconds, _batchSize);

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
            await Task.Delay(_pollingInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Outbox Publisher stopped");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        var messages = await _outboxStore.GetPendingMessagesAsync(_batchSize, cancellationToken);
        if (messages.Count == 0) return;

        _logger.LogDebug("Processing {Count} outbox messages", messages.Count);
        var tasks = messages.Select(message => ProcessMessageAsync(message, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task ProcessMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Publishing outbox message {MessageId} of type {MessageType}", message.MessageId, message.MessageType);
            await _outboxStore.MarkAsPublishedAsync(message.MessageId, cancellationToken);
            _logger.LogInformation("Published outbox message {MessageId}", message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish outbox message {MessageId}", message.MessageId);
            await _outboxStore.MarkAsFailedAsync(message.MessageId, ex.Message, cancellationToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Outbox Publisher is stopping...");
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

