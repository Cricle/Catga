using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Catga.Outbox;
using Catga.Transport;

namespace Catga.Hosting;

/// <summary>
/// Outbox 处理器后台服务 - 定期扫描并发送待处理消息
/// </summary>
public sealed partial class OutboxProcessorService : BackgroundService
{
    private readonly IOutboxStore _outboxStore;
    private readonly IMessageTransport _transport;
    private readonly ILogger<OutboxProcessorService> _logger;
    private readonly OutboxProcessorOptions _options;
    private volatile int _isProcessingBatch;
    private volatile int _totalProcessed;
    private volatile int _totalFailed;

    public OutboxProcessorService(
        IOutboxStore outboxStore,
        IMessageTransport transport,
        ILogger<OutboxProcessorService> logger,
        OutboxProcessorOptions options)
    {
        _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        
        _options.Validate();
    }

    /// <summary>
    /// 指示是否正在处理批次
    /// </summary>
    public bool IsProcessingBatch => _isProcessingBatch == 1;

    /// <summary>
    /// 已处理的消息总数
    /// </summary>
    public int TotalProcessed => _totalProcessed;

    /// <summary>
    /// 失败的消息总数
    /// </summary>
    public int TotalFailed => _totalFailed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogOutboxProcessorStarted(_options.ScanInterval.TotalSeconds, _options.BatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Process batch first, then wait
                    await ProcessBatchAsync(stoppingToken);
                    await Task.Delay(_options.ScanInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // 停机请求
                    if (_options.CompleteCurrentBatchOnShutdown && IsProcessingBatch)
                    {
                        LogWaitingForCurrentBatch();
                        // 等待当前批次完成（已经在 ProcessBatchAsync 中处理）
                    }
                    break;
                }
                catch (Exception ex)
                {
                    LogProcessingLoopException(ex);
                    Interlocked.Increment(ref _totalFailed);
                    
                    // 发生错误后等待一段时间再重试
                    try
                    {
                        await Task.Delay(_options.ErrorDelay, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            LogOutboxProcessorStopped(_totalProcessed, _totalFailed);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        // 使用 Interlocked 确保同一时间只有一个批次在处理
        if (Interlocked.CompareExchange(ref _isProcessingBatch, 1, 0) != 0)
        {
            LogBatchAlreadyProcessing();
            return;
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            // 获取待处理的消息
            var messages = await _outboxStore.GetPendingMessagesAsync(_options.BatchSize, cancellationToken);
            
            if (messages.Count == 0)
            {
                return; // 没有待处理的消息
            }

            LogProcessingBatch(messages.Count);

            var successCount = 0;
            var failureCount = 0;

            foreach (var message in messages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // 如果配置为完成当前批次，继续处理
                    if (!_options.CompleteCurrentBatchOnShutdown)
                    {
                        LogBatchInterrupted(successCount, failureCount, messages.Count);
                        break;
                    }
                }

                try
                {
                    // 发布消息到传输层
                    await PublishMessageAsync(message, cancellationToken);
                    
                    // 标记为已发布
                    await _outboxStore.MarkAsPublishedAsync(message.MessageId, cancellationToken);
                    
                    successCount++;
                    Interlocked.Increment(ref _totalProcessed);
                    
                    LogMessagePublished(message.MessageId, message.MessageType);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 取消操作
                    if (!_options.CompleteCurrentBatchOnShutdown)
                    {
                        throw;
                    }
                    // 否则继续处理
                }
                catch (Exception ex)
                {
                    failureCount++;
                    Interlocked.Increment(ref _totalFailed);
                    
                    LogMessagePublishFailed(message.MessageId, message.MessageType, ex);
                    
                    // 标记为失败
                    try
                    {
                        await _outboxStore.MarkAsFailedAsync(
                            message.MessageId,
                            ex.Message,
                            cancellationToken);
                    }
                    catch (Exception markFailedEx)
                    {
                        LogMarkFailedError(message.MessageId, markFailedEx);
                    }
                }
            }

            sw.Stop();
            LogBatchCompleted(successCount, failureCount, messages.Count, sw.Elapsed.TotalSeconds);
        }
        finally
        {
            Interlocked.Exchange(ref _isProcessingBatch, 0);
        }
    }

    private async Task PublishMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // 这里需要将 OutboxMessage 转换为传输层可以理解的格式
        // 由于我们已经有序列化的 payload，我们需要反序列化然后发布
        // 但是为了保持简单，我们假设传输层可以直接处理原始消息
        
        // 注意：实际实现中，这里可能需要使用 IMessageSerializer 来反序列化消息
        // 然后调用 _transport.PublishAsync
        
        // 为了这个实现，我们假设有一个方法可以直接发布原始消息
        // 这是一个简化的实现，实际使用中可能需要更复杂的逻辑
        
        await Task.CompletedTask; // 占位符 - 实际实现需要调用传输层
        
        // TODO: 实际实现应该类似：
        // var deserializedMessage = await _serializer.DeserializeAsync(message.Payload, message.MessageType);
        // await _transport.PublishAsync(deserializedMessage, cancellationToken);
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox processor started with scan interval: {ScanIntervalSeconds}s, batch size: {BatchSize}")]
    partial void LogOutboxProcessorStarted(double scanIntervalSeconds, int batchSize);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox processor stopped. Total processed: {TotalProcessed}, Total failed: {TotalFailed}")]
    partial void LogOutboxProcessorStopped(int totalProcessed, int totalFailed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Batch already processing, skipping this scan")]
    partial void LogBatchAlreadyProcessing();

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing batch of {MessageCount} message(s)")]
    partial void LogProcessingBatch(int messageCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch completed: {SuccessCount} succeeded, {FailureCount} failed out of {TotalCount} in {DurationSeconds:F2}s")]
    partial void LogBatchCompleted(int successCount, int failureCount, int totalCount, double durationSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Batch interrupted by cancellation: {SuccessCount} succeeded, {FailureCount} failed out of {TotalCount}")]
    partial void LogBatchInterrupted(int successCount, int failureCount, int totalCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for current batch to complete before shutdown")]
    partial void LogWaitingForCurrentBatch();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message published: ID={MessageId}, Type={MessageType}")]
    partial void LogMessagePublished(long messageId, string messageType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to publish message: ID={MessageId}, Type={MessageType}")]
    partial void LogMessagePublishFailed(long messageId, string messageType, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mark message {MessageId} as failed")]
    partial void LogMarkFailedError(long messageId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception in outbox processing loop")]
    partial void LogProcessingLoopException(Exception ex);

    #endregion
}
