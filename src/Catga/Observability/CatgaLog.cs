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
}

