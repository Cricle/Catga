using Microsoft.Extensions.Logging;

namespace Catga.Observability;

/// <summary>High-performance logging using source-generated LoggerMessage</summary>
internal static partial class CatgaLog
{
    // Message
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Publishing message {MessageType} [MessageId={MessageId}]")]
    public static partial void MessagePublishing(ILogger logger, string messageType, long? messageId);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Message published {MessageType} [MessageId={MessageId}, Duration={DurationMs}ms]")]
    public static partial void MessagePublished(ILogger logger, string messageType, long? messageId, double durationMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Message publish failed {MessageType} [MessageId={MessageId}]")]
    public static partial void MessagePublishFailed(ILogger logger, Exception? exception, string messageType, long? messageId);

    // Command
    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug, Message = "Executing command {CommandType} [MessageId={MessageId}]")]
    public static partial void CommandExecuting(ILogger logger, string commandType, long? messageId);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Command executed {CommandType} [MessageId={MessageId}, Duration={DurationMs}ms]")]
    public static partial void CommandExecuted(ILogger logger, string commandType, long? messageId, double durationMs);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "Command failed {CommandType} [MessageId={MessageId}]")]
    public static partial void CommandFailed(ILogger logger, Exception? exception, string commandType, long? messageId);

    // Event
    [LoggerMessage(EventId = 3000, Level = LogLevel.Debug, Message = "Publishing event {EventType} [MessageId={MessageId}]")]
    public static partial void EventPublishing(ILogger logger, string eventType, long? messageId);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Event published {EventType} [MessageId={MessageId}, HandlerCount={HandlerCount}]")]
    public static partial void EventPublished(ILogger logger, string eventType, long? messageId, int handlerCount);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "Event handler failed {EventType} [Handler={HandlerType}]")]
    public static partial void EventHandlerFailed(ILogger logger, Exception exception, string eventType, string handlerType);

    // Pipeline
    [LoggerMessage(EventId = 4000, Level = LogLevel.Debug, Message = "Pipeline behavior executing {BehaviorType} for {RequestType}")]
    public static partial void PipelineBehaviorExecuting(ILogger logger, string behaviorType, string requestType);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Debug, Message = "Pipeline behavior completed {BehaviorType} [Duration={DurationMs}ms]")]
    public static partial void PipelineBehaviorCompleted(ILogger logger, string behaviorType, double durationMs);

    // Idempotency
    [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "Message already processed {MessageId}, returning cached result")]
    public static partial void MessageIdempotent(ILogger logger, long messageId);

    // Dead Letter
    [LoggerMessage(EventId = 6000, Level = LogLevel.Error, Message = "Message moved to DLQ {MessageType} [MessageId={MessageId}, Reason={Reason}]")]
    public static partial void MessageMovedToDLQ(ILogger logger, string messageType, long messageId, string reason);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Error, Message = "Message moved to DLQ {MessageType} [MessageId={MessageId}, Reason={Reason}, RetryCount={RetryCount}]")]
    public static partial void MessageMovedToDLQ(ILogger logger, string messageType, long messageId, string reason, int retryCount);

    // Transport
    [LoggerMessage(EventId = 7000, Level = LogLevel.Information, Message = "Transport initialized {TransportType}")]
    public static partial void TransportInitialized(ILogger logger, string transportType);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning, Message = "Transport connection lost {TransportType}, reconnecting...")]
    public static partial void TransportConnectionLost(ILogger logger, string transportType);

    // Performance
    [LoggerMessage(EventId = 8000, Level = LogLevel.Warning, Message = "Slow operation detected {OperationType} [Duration={DurationMs}ms]")]
    public static partial void SlowOperationDetected(ILogger logger, string operationType, double durationMs);

    // Outbox
    [LoggerMessage(EventId = 5200, Level = LogLevel.Debug, Message = "Added message {MessageId} to outbox")]
    public static partial void OutboxAdded(ILogger logger, long messageId);

    [LoggerMessage(EventId = 5203, Level = LogLevel.Debug, Message = "Marked message {MessageId} as published")]
    public static partial void OutboxMarkedPublished(ILogger logger, long messageId);

    [LoggerMessage(EventId = 5205, Level = LogLevel.Warning, Message = "Message {MessageId} failed after {RetryCount} retries")]
    public static partial void OutboxMessageFailedAfterRetries(ILogger logger, long messageId, int retryCount);

    // Inbox
    [LoggerMessage(EventId = 5100, Level = LogLevel.Debug, Message = "Locked message {MessageId} for processing")]
    public static partial void InboxLocked(ILogger logger, long messageId);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Debug, Message = "Message {MessageId} already processed or locked")]
    public static partial void InboxAlreadyProcessedOrLocked(ILogger logger, long messageId);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Debug, Message = "Marked message {MessageId} as processed")]
    public static partial void InboxMarkedProcessed(ILogger logger, long messageId);

    // Inbox Behavior
    [LoggerMessage(EventId = 4100, Level = LogLevel.Debug, Message = "No MessageId found for {RequestType}, skipping inbox check")]
    public static partial void InboxNoMessageId(ILogger logger, string requestType);

    [LoggerMessage(EventId = 4101, Level = LogLevel.Information, Message = "Message {MessageId} has already been processed")]
    public static partial void InboxAlreadyProcessed(ILogger logger, long messageId);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Warning, Message = "Failed to acquire lock for message {MessageId}")]
    public static partial void InboxLockFailed(ILogger logger, long messageId);

    // Outbox Behavior
    [LoggerMessage(EventId = 4200, Level = LogLevel.Debug, Message = "[Outbox] Saved message {MessageId}")]
    public static partial void OutboxSaved(ILogger logger, long messageId);

    [LoggerMessage(EventId = 4201, Level = LogLevel.Information, Message = "[Outbox] Published message {MessageId}")]
    public static partial void OutboxPublished(ILogger logger, long messageId);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Error, Message = "[Outbox] Failed to publish message {MessageId}")]
    public static partial void OutboxPublishFailed(ILogger logger, Exception? exception, long messageId);

    // Batching
    [LoggerMessage(EventId = 4300, Level = LogLevel.Warning, Message = "Mediator batch queue overflow for {RequestType}")]
    public static partial void MediatorBatchOverflow(ILogger logger, string requestType);

    [LoggerMessage(EventId = 4301, Level = LogLevel.Error, Message = "Mediator auto-batch timer failed for {RequestType}")]
    public static partial void MediatorBatchTimerError(ILogger logger, Exception? exception, string requestType);
}
