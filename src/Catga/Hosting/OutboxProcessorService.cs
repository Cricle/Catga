using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Catga.Abstractions;
using Catga.Outbox;
using Catga.Transport;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Catga.Hosting;

/// <summary>
/// Outbox 处理器后台服务 - 定期扫描并发送待处理消息
/// </summary>
public sealed partial class OutboxProcessorService : BackgroundService
{
    private readonly IOutboxStore _outboxStore;
    private readonly IMessageTransport _transport;
    private readonly IMessageSerializer _serializer;
    private readonly IMessageTypeRegistry _messageTypeRegistry;
    private readonly ILogger<OutboxProcessorService> _logger;
    private readonly OutboxProcessorOptions _options;
    private volatile int _isProcessingBatch;
    private volatile int _totalProcessed;
    private volatile int _totalFailed;

    public OutboxProcessorService(
        IOutboxStore outboxStore,
        IMessageTransport transport,
        IMessageSerializer serializer,
        IMessageTypeRegistry messageTypeRegistry,
        ILogger<OutboxProcessorService> logger,
        OutboxProcessorOptions options)
    {
        _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _messageTypeRegistry = messageTypeRegistry ?? throw new ArgumentNullException(nameof(messageTypeRegistry));
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
            
            // Get pending messages — filter out scheduled ones not yet due
            var allMessages = await _outboxStore.GetPendingMessagesAsync(_options.BatchSize, cancellationToken);
            var messages = allMessages.Where(m => m.IsReadyToDeliver).ToList();
            
            if (messages.Count == 0)
            {
                return;
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
        // 从完整类型名称获取 Type 对象
        // 格式应该是: "Namespace.TypeName, AssemblyName"
        var messageType = _messageTypeRegistry.Resolve(message.MessageType);
        if (messageType == null)
        {
            throw new InvalidOperationException(
                $"Cannot resolve message type: {message.MessageType}. " +
                "Ensure the message type is registered and properly referenced.");
        }

        try
        {
            // 使用 IMessageSerializer 反序列化消息
            var deserializedMessage = _serializer.Deserialize(message.Payload, messageType);
            if (deserializedMessage == null)
            {
                throw new InvalidOperationException(
                    $"Deserialization returned null for message {message.MessageId} of type {message.MessageType}");
            }

            // 创建传输上下文
            var context = new TransportContext
            {
                MessageId = message.MessageId,
                CorrelationId = message.CorrelationId
            };

            await InvokePublishAsync(_transport, messageType, deserializedMessage, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to publish outbox message {message.MessageId} of type {message.MessageType}",
                ex);
        }
    }

    [UnconditionalSuppressMessage("AOT", "IL2060", Justification = "Outbox dispatch resolves already-registered message types at runtime.")]
    [UnconditionalSuppressMessage("AOT", "IL2075", Justification = "PublishAsync generic method is resolved from IMessageTransport by contract.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Runtime outbox dispatch intentionally closes PublishAsync<T> using registered message types.")]
    private static Task InvokePublishAsync(
        IMessageTransport transport,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type messageType,
        object message,
        TransportContext context,
        CancellationToken cancellationToken)
    {
        var publishMethod = typeof(IMessageTransport)
            .GetMethod(nameof(IMessageTransport.PublishAsync), BindingFlags.Instance | BindingFlags.Public);

        if (publishMethod is null)
        {
            throw new InvalidOperationException(
                $"Cannot find {nameof(IMessageTransport.PublishAsync)} method on transport contract.");
        }

        var closedMethod = publishMethod.MakeGenericMethod(messageType);
        var publishTask = closedMethod.Invoke(transport, new[] { message, context, cancellationToken });
        return publishTask as Task
            ?? throw new InvalidOperationException("PublishAsync did not return a Task.");
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
