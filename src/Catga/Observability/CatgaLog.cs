using Microsoft.Extensions.Logging;

namespace Catga.Observability;

/// <summary>High-performance logging using source-generated LoggerMessage</summary>
internal static partial class CatgaLog
{
    // Message Transport Logs
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Publishing message {MessageType} [MessageId={MessageId}, QoS={QoS}]")]
    public static partial void MessagePublishing(ILogger logger, string messageType, long? messageId, string qoS);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Message published {MessageType} [MessageId={MessageId}, Duration={DurationMs}ms]")]
    public static partial void MessagePublished(ILogger logger, string messageType, long? messageId, double durationMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Message publish failed {MessageType} [MessageId={MessageId}, Error={Error}]")]
    public static partial void MessagePublishFailed(ILogger logger, Exception? exception, string messageType, long? messageId, string error);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Message received {MessageType} [MessageId={MessageId}]")]
    public static partial void MessageReceived(ILogger logger, string messageType, long? messageId);

    // Command/Query Logs
    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug, Message = "Executing command {CommandType} [MessageId={MessageId}, CorrelationId={CorrelationId}]")]
    public static partial void CommandExecuting(ILogger logger, string commandType, long? messageId, long? correlationId);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Command executed {CommandType} [MessageId={MessageId}, Duration={DurationMs}ms, Success={Success}]")]
    public static partial void CommandExecuted(ILogger logger, string commandType, long? messageId, double durationMs, bool success);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "Command failed {CommandType} [MessageId={MessageId}, Error={Error}]")]
    public static partial void CommandFailed(ILogger logger, Exception? exception, string commandType, long? messageId, string error);

    [LoggerMessage(EventId = 2100, Level = LogLevel.Debug, Message = "Executing query {QueryType} [MessageId={MessageId}, CorrelationId={CorrelationId}]")]
    public static partial void QueryExecuting(ILogger logger, string queryType, long? messageId, long? correlationId);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "Query executed {QueryType} [MessageId={MessageId}, Duration={DurationMs}ms]")]
    public static partial void QueryExecuted(ILogger logger, string queryType, long? messageId, double durationMs);

    // Event Logs
    [LoggerMessage(EventId = 3000, Level = LogLevel.Debug, Message = "Publishing event {EventType} [MessageId={MessageId}]")]
    public static partial void EventPublishing(ILogger logger, string eventType, long? messageId);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Event published {EventType} [MessageId={MessageId}, HandlerCount={HandlerCount}]")]
    public static partial void EventPublished(ILogger logger, string eventType, long? messageId, int handlerCount);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "Event handler failed {EventType} [MessageId={MessageId}, Handler={HandlerType}]")]
    public static partial void EventHandlerFailed(ILogger logger, Exception exception, string eventType, long? messageId, string handlerType);

    // Pipeline Logs
    [LoggerMessage(EventId = 4000, Level = LogLevel.Debug, Message = "Pipeline behavior executing {BehaviorType} for {RequestType}")]
    public static partial void PipelineBehaviorExecuting(ILogger logger, string behaviorType, string requestType);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Debug, Message = "Pipeline behavior completed {BehaviorType} [Duration={DurationMs}ms]")]
    public static partial void PipelineBehaviorCompleted(ILogger logger, string behaviorType, double durationMs);

    // Idempotency Logs
    [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "Message already processed {MessageId}, returning cached result")]
    public static partial void MessageIdempotent(ILogger logger, long messageId);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Debug, Message = "Caching result for message {MessageId}")]
    public static partial void ResultCached(ILogger logger, long messageId);

    // Dead Letter Queue Logs
    [LoggerMessage(EventId = 6000, Level = LogLevel.Error, Message = "Message moved to DLQ {MessageType} [MessageId={MessageId}, Reason={Reason}, RetryCount={RetryCount}]")]
    public static partial void MessageMovedToDLQ(ILogger logger, string messageType, long messageId, string reason, int retryCount);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "DLQ is full, dropping oldest message [Size={Size}, Max={MaxSize}]")]
    public static partial void DLQFull(ILogger logger, int size, int maxSize);

    // Transport Logs
    [LoggerMessage(EventId = 7000, Level = LogLevel.Information, Message = "Transport initialized {TransportType} [Name={TransportName}]")]
    public static partial void TransportInitialized(ILogger logger, string transportType, string transportName);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning, Message = "Transport connection lost {TransportType}, reconnecting...")]
    public static partial void TransportConnectionLost(ILogger logger, string transportType);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Information, Message = "Transport reconnected {TransportType}")]
    public static partial void TransportReconnected(ILogger logger, string transportType);

    // Performance Logs
    [LoggerMessage(EventId = 8000, Level = LogLevel.Warning, Message = "Slow operation detected {OperationType} [Duration={DurationMs}ms, Threshold={ThresholdMs}ms]")]
    public static partial void SlowOperationDetected(ILogger logger, string operationType, double durationMs, double thresholdMs);

    [LoggerMessage(EventId = 8001, Level = LogLevel.Information, Message = "High throughput detected [MessagesPerSecond={MessagesPerSecond}]")]
    public static partial void HighThroughputDetected(ILogger logger, long messagesPerSecond);


    // Inbox Behavior Logs (4100-4199)
    [LoggerMessage(EventId = 4100, Level = LogLevel.Debug, Message = "No MessageId found for {RequestType}, skipping inbox check")]
    public static partial void InboxNoMessageId(ILogger logger, string RequestType);

    [LoggerMessage(EventId = 4101, Level = LogLevel.Information, Message = "Message {MessageId} has already been processed, returning cached result")]
    public static partial void InboxAlreadyProcessed(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Warning, Message = "Failed to deserialize cached result for message {MessageId}, returning default success")]
    public static partial void InboxCachedResultDeserializeFailed(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Warning, Message = "Failed to acquire lock for message {MessageId}, another instance may be processing it")]
    public static partial void InboxLockFailed(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 4104, Level = LogLevel.Debug, Message = "Marked message {MessageId} as processed in inbox")]
    public static partial void InboxProcessed(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 4105, Level = LogLevel.Error, Message = "Error processing message {MessageId} in inbox")]
    public static partial void InboxProcessingError(ILogger logger, Exception? exception, long MessageId);

    [LoggerMessage(EventId = 4106, Level = LogLevel.Error, Message = "Error in inbox behavior for message {MessageId}")]
    public static partial void InboxBehaviorError(ILogger logger, Exception? exception, long MessageId);

    // Outbox Behavior Logs (4200-4299)
    [LoggerMessage(EventId = 4200, Level = LogLevel.Debug, Message = "[Outbox] Saved message {MessageId} to {Store}")]
    public static partial void OutboxSaved(ILogger logger, long MessageId, string Store);

    [LoggerMessage(EventId = 4201, Level = LogLevel.Information, Message = "[Outbox] Published message {MessageId} via {Transport}")]
    public static partial void OutboxPublished(ILogger logger, long MessageId, string Transport);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Error, Message = "[Outbox] Failed to publish message {MessageId}, marked for retry")]
    public static partial void OutboxPublishFailed(ILogger logger, Exception? exception, long MessageId);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Error, Message = "[Outbox] Error in outbox behavior for {RequestType}")]
    public static partial void OutboxBehaviorError(ILogger logger, Exception? exception, string RequestType);

    // AutoBatching Behavior Logs (4300-4399)
    [LoggerMessage(EventId = 4300, Level = LogLevel.Warning, Message = "Mediator batch queue overflow for {RequestType} shard {Key}")]
    public static partial void MediatorBatchOverflow(ILogger logger, string RequestType, string Key);

    [LoggerMessage(EventId = 4301, Level = LogLevel.Error, Message = "Mediator auto-batch timer failed for {RequestType}")]
    public static partial void MediatorBatchTimerError(ILogger logger, Exception? exception, string RequestType);

    [LoggerMessage(EventId = 4302, Level = LogLevel.Error, Message = "Mediator auto-batch entry failed for {RequestType}")]
    public static partial void MediatorBatchEntryError(ILogger logger, Exception? exception, string RequestType);

    [LoggerMessage(EventId = 4303, Level = LogLevel.Error, Message = "Mediator auto-batch flush failed for {RequestType}")]
    public static partial void MediatorBatchFlushError(ILogger logger, Exception? exception, string RequestType);

    // Idempotency Logs (5300-5399)
    [LoggerMessage(EventId = 5300, Level = LogLevel.Warning, Message = "Type mismatch for message {MessageId}: expected {Expected}, got {Actual}")]
    public static partial void IdempotencyTypeMismatch(ILogger logger, long MessageId, string? Expected, string? Actual);

    [LoggerMessage(EventId = 5301, Level = LogLevel.Debug, Message = "Marked message {MessageId} as processed in idempotency store")]
    public static partial void IdempotencyMarkedProcessed(ILogger logger, long MessageId);

    // Dead Letter Queue
    [LoggerMessage(EventId = 6002, Level = LogLevel.Error, Message = "Failed to send message to DeadLetterQueue")]
    public static partial void DLQSendFailed(ILogger logger, Exception? exception);

    // Redis Outbox Persistence Logs (9000-9099)
    [LoggerMessage(EventId = 9000, Level = LogLevel.Debug, Message = "Outbox message added {MessageId}")]
    public static partial void OutboxAdded(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Error, Message = "Outbox message add failed {MessageId}")]
    public static partial void OutboxAddFailed(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Warning, Message = "Outbox message deserialize failed {MessageId}")]
    public static partial void OutboxDeserializeFailed(ILogger logger, Exception? exception, long MessageId);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning, Message = "Outbox message not found {MessageId}")]
    public static partial void OutboxMessageNotFound(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 9004, Level = LogLevel.Debug, Message = "Outbox message marked published {MessageId}")]
    public static partial void OutboxMarkedPublished(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 9005, Level = LogLevel.Error, Message = "Outbox message failed after retries {MessageId} [RetryCount={RetryCount}, Error={Error}]")]
    public static partial void OutboxMessageFailedAfterRetries(ILogger logger, long MessageId, int RetryCount, string? Error);

    [LoggerMessage(EventId = 9006, Level = LogLevel.Debug, Message = "Outbox message retry {MessageId} [RetryCount={RetryCount}, MaxRetries={MaxRetries}, Error={Error}]")]
    public static partial void OutboxMessageRetry(ILogger logger, long MessageId, int RetryCount, int MaxRetries, string? Error);

    [LoggerMessage(EventId = 9007, Level = LogLevel.Debug, Message = "Outbox cleanup completed {Count} messages")]
    public static partial void OutboxCleanup(ILogger logger, long Count);

    // Redis Inbox Persistence Logs (9100-9199)
    [LoggerMessage(EventId = 9100, Level = LogLevel.Debug, Message = "Inbox message locked {MessageId}")]
    public static partial void InboxLocked(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 9101, Level = LogLevel.Debug, Message = "Inbox message already processed or locked {MessageId}")]
    public static partial void InboxAlreadyProcessedOrLocked(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Debug, Message = "Inbox message marked processed {MessageId}")]
    public static partial void InboxMarkedProcessed(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Debug, Message = "Inbox lock released {MessageId}")]
    public static partial void InboxReleasedLock(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 9104, Level = LogLevel.Debug, Message = "Inbox TTL cleanup - Redis handles via expiry")]
    public static partial void InboxTTL(ILogger logger);
}

