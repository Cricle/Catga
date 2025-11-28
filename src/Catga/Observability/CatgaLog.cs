using Microsoft.Extensions.Logging;

namespace Catga.Observability;

/// <summary>High-performance logging using source-generated LoggerMessage</summary>
public static partial class CatgaLog
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

    // NATS Transport Detailed Logs (7100-7199)
    [LoggerMessage(EventId = 7100, Level = LogLevel.Debug, Message = "Published to NATS Core (QoS 0 - fire-and-forget): {MessageId}")]
    public static partial void NatsPublishedCore(ILogger logger, long? messageId);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Debug, Message = "Published to JetStream (QoS 1 - at-least-once): {MessageId}, Seq: {Seq}, Duplicate: {Dup}")]
    public static partial void NatsPublishedQoS1(ILogger logger, long? messageId, ulong Seq, bool Dup);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Debug, Message = "Published to JetStream (QoS 2 - exactly-once): {MessageId}, Seq: {Seq}, Duplicate: {Dup}")]
    public static partial void NatsPublishedQoS2(ILogger logger, long? messageId, ulong Seq, bool Dup);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Error, Message = "NATS publish failed for subject {Subject}, MessageId: {MessageId}")]
    public static partial void NatsPublishFailed(ILogger logger, Exception? exception, string Subject, long? MessageId);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Debug, Message = "Published batch item to JetStream: Seq={Seq}, Dup={Dup}")]
    public static partial void NatsBatchPublishedJetStream(ILogger logger, ulong Seq, bool Dup);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Error, Message = "NATS batch publish failed for subject {Subject}")]
    public static partial void NatsBatchPublishFailed(ILogger logger, Exception? exception, string Subject);

    [LoggerMessage(EventId = 7106, Level = LogLevel.Warning, Message = "Received empty message from subject {Subject}")]
    public static partial void NatsEmptyMessage(ILogger logger, string Subject);

    [LoggerMessage(EventId = 7107, Level = LogLevel.Warning, Message = "Failed to deserialize message from subject {Subject}")]
    public static partial void NatsDeserializeFailed(ILogger logger, string Subject);

    [LoggerMessage(EventId = 7108, Level = LogLevel.Debug, Message = "Dropped duplicate message {MessageId} (QoS={QoS}) on subject {Subject}")]
    public static partial void NatsDroppedDuplicate(ILogger logger, long? MessageId, int QoS, string Subject);

    [LoggerMessage(EventId = 7109, Level = LogLevel.Error, Message = "Error processing message from subject {Subject}")]
    public static partial void NatsProcessingError(ILogger logger, Exception? exception, string Subject);

    // Redis Outbox Persistence Logs (5200-5299)
    [LoggerMessage(EventId = 5200, Level = LogLevel.Debug, Message = "Added message {MessageId} to outbox persistence")]
    public static partial void OutboxAdded(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 5201, Level = LogLevel.Warning, Message = "Failed to add message {MessageId} to outbox (transaction failed)")]
    public static partial void OutboxAddFailed(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Error, Message = "Failed to deserialize outbox message {MessageId}")]
    public static partial void OutboxDeserializeFailed(ILogger logger, Exception? exception, long MessageId);

    [LoggerMessage(EventId = 5203, Level = LogLevel.Debug, Message = "Marked message {MessageId} as published")]
    public static partial void OutboxMarkedPublished(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 5204, Level = LogLevel.Warning, Message = "Message {MessageId} not found in outbox")]
    public static partial void OutboxMessageNotFound(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 5205, Level = LogLevel.Warning, Message = "Message {MessageId} failed after {RetryCount} retries: {Error}")]
    public static partial void OutboxMessageFailedAfterRetries(ILogger logger, long MessageId, int RetryCount, string Error);

    [LoggerMessage(EventId = 5206, Level = LogLevel.Debug, Message = "Message {MessageId} failed (retry {RetryCount}/{MaxRetries}): {Error}")]
    public static partial void OutboxMessageRetry(ILogger logger, long MessageId, int RetryCount, int MaxRetries, string Error);

    [LoggerMessage(EventId = 5207, Level = LogLevel.Information, Message = "Cleaned up {Count} old outbox entries")]
    public static partial void OutboxCleanup(ILogger logger, long Count);

    // Redis Inbox Persistence Logs (5100-5199)
    [LoggerMessage(EventId = 5100, Level = LogLevel.Debug, Message = "Locked message {MessageId} for processing")]
    public static partial void InboxLocked(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Debug, Message = "Message {MessageId} already processed or locked")]
    public static partial void InboxAlreadyProcessedOrLocked(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Debug, Message = "Marked message {MessageId} as processed")]
    public static partial void InboxMarkedProcessed(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Debug, Message = "Released lock on message {MessageId}")]
    public static partial void InboxReleasedLock(ILogger logger, long MessageId);

    [LoggerMessage(EventId = 5104, Level = LogLevel.Debug, Message = "Redis inbox uses TTL for cleanup")]
    public static partial void InboxTTL(ILogger logger);

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

    [LoggerMessage(EventId = 5301, Level = LogLevel.Debug, Message = "Marked message {MessageId} as processed in Redis idempotency store")]
    public static partial void IdempotencyMarkedProcessed(ILogger logger, long MessageId);

    // Dead Letter Queue
    [LoggerMessage(EventId = 6002, Level = LogLevel.Error, Message = "Failed to send message to DeadLetterQueue")]
    public static partial void DLQSendFailed(ILogger logger, Exception? exception);

    // InMemory Transport
    [LoggerMessage(EventId = 7010, Level = LogLevel.Warning, Message = "QoS 0 message processing failed, discarding. MessageId: {MessageId}, Type: {MessageType}")]
    public static partial void InMemoryQoS0ProcessingFailed(ILogger logger, Exception? exception, long? MessageId, string MessageType);
}

