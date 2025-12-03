using Catga.Cluster;

namespace OrderSystem.Api.Services;

/// <summary>
/// Outbox processor that only runs on the leader node.
/// Demonstrates SingletonTaskRunner for cluster-aware background tasks.
/// </summary>
public sealed class OutboxProcessorTask : SingletonTaskRunner
{
    private readonly ILogger<OutboxProcessorTask> _logger;
    private int _processedCount;

    public OutboxProcessorTask(
        IClusterCoordinator coordinator,
        ILogger<OutboxProcessorTask> logger)
        : base(coordinator, logger, TimeSpan.FromSeconds(2))
    {
        _logger = logger;
    }

    protected override string TaskName => "OutboxProcessor";

    protected override async Task ExecuteLeaderTaskAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("📤 [OutboxProcessor] Started on leader node");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Simulate processing outbox messages
                _processedCount++;
                _logger.LogDebug("📤 [OutboxProcessor] Processing batch #{Count}", _processedCount);

                // In real implementation:
                // var pending = await _outboxStore.GetPendingMessagesAsync(100, stoppingToken);
                // foreach (var msg in pending)
                // {
                //     await _transport.PublishAsync(msg, stoppingToken);
                //     await _outboxStore.MarkAsPublishedAsync(msg.MessageId, stoppingToken);
                // }

                await Task.Delay(5000, stoppingToken); // Process every 5 seconds
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "📤 [OutboxProcessor] Error processing batch");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("📤 [OutboxProcessor] Stopped (processed {Count} batches)", _processedCount);
    }
}
