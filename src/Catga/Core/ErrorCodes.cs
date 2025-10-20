namespace Catga.Core;

/// <summary>
/// Catga error codes - structured error identification without exceptions
/// </summary>
public static class ErrorCodes
{
    // === Message Processing Errors (1xxx) ===

    /// <summary>Message validation failed</summary>
    public const string MessageValidationFailed = "CATGA_1001";

    /// <summary>Message ID is invalid or missing</summary>
    public const string InvalidMessageId = "CATGA_1002";

    /// <summary>Message already processed (idempotency check)</summary>
    public const string MessageAlreadyProcessed = "CATGA_1003";

    /// <summary>Message processing timeout</summary>
    public const string MessageTimeout = "CATGA_1004";

    /// <summary>Message handler not found</summary>
    public const string HandlerNotFound = "CATGA_1005";

    /// <summary>Message deserialization failed</summary>
    public const string DeserializationFailed = "CATGA_1006";

    /// <summary>Message serialization failed</summary>
    public const string SerializationFailed = "CATGA_1007";

    // === Inbox/Outbox Errors (2xxx) ===

    /// <summary>Failed to acquire inbox lock</summary>
    public const string InboxLockFailed = "CATGA_2001";

    /// <summary>Inbox persistence operation failed</summary>
    public const string InboxPersistenceFailed = "CATGA_2002";

    /// <summary>Outbox persistence operation failed</summary>
    public const string OutboxPersistenceFailed = "CATGA_2003";

    /// <summary>Outbox publish failed</summary>
    public const string OutboxPublishFailed = "CATGA_2004";

    /// <summary>Message already exists in inbox</summary>
    public const string InboxDuplicateMessage = "CATGA_2005";

    // === Transport Errors (3xxx) ===

    /// <summary>Transport connection failed</summary>
    public const string TransportConnectionFailed = "CATGA_3001";

    /// <summary>Transport publish failed</summary>
    public const string TransportPublishFailed = "CATGA_3002";

    /// <summary>Transport subscribe failed</summary>
    public const string TransportSubscribeFailed = "CATGA_3003";

    /// <summary>Transport not available</summary>
    public const string TransportUnavailable = "CATGA_3004";

    /// <summary>Network timeout</summary>
    public const string NetworkTimeout = "CATGA_3005";

    // === Persistence Errors (4xxx) ===

    /// <summary>Event store write failed</summary>
    public const string EventStoreWriteFailed = "CATGA_4001";

    /// <summary>Event store read failed</summary>
    public const string EventStoreReadFailed = "CATGA_4002";

    /// <summary>Event stream not found</summary>
    public const string EventStreamNotFound = "CATGA_4003";

    /// <summary>Optimistic concurrency conflict</summary>
    public const string ConcurrencyConflict = "CATGA_4004";

    /// <summary>Idempotency store operation failed</summary>
    public const string IdempotencyStoreFailed = "CATGA_4005";

    // === Configuration Errors (5xxx) ===

    /// <summary>Serializer not registered</summary>
    public const string SerializerNotRegistered = "CATGA_5001";

    /// <summary>Transport not registered</summary>
    public const string TransportNotRegistered = "CATGA_5002";

    /// <summary>Invalid configuration</summary>
    public const string InvalidConfiguration = "CATGA_5003";

    /// <summary>Required service not registered</summary>
    public const string ServiceNotRegistered = "CATGA_5004";

    // === Pipeline Errors (6xxx) ===

    /// <summary>Pipeline execution failed</summary>
    public const string PipelineExecutionFailed = "CATGA_6001";

    /// <summary>Behavior execution failed</summary>
    public const string BehaviorExecutionFailed = "CATGA_6002";

    /// <summary>Handler execution failed</summary>
    public const string HandlerExecutionFailed = "CATGA_6003";

    /// <summary>Validation behavior failed</summary>
    public const string ValidationFailed = "CATGA_6004";

    /// <summary>Retry limit exceeded</summary>
    public const string RetryLimitExceeded = "CATGA_6005";

    // === System Errors (9xxx) ===

    /// <summary>Unknown error</summary>
    public const string UnknownError = "CATGA_9000";

    /// <summary>Operation cancelled</summary>
    public const string OperationCancelled = "CATGA_9001";

    /// <summary>Clock moved backwards (Snowflake ID generation)</summary>
    public const string ClockMovedBackwards = "CATGA_9002";

    /// <summary>Resource exhausted (memory, connections, etc.)</summary>
    public const string ResourceExhausted = "CATGA_9003";

    /// <summary>Internal error</summary>
    public const string InternalError = "CATGA_9999";
}

/// <summary>
/// Error information - structured error without exception allocation
/// </summary>
public readonly record struct ErrorInfo
{
    /// <summary>Error code (e.g., CATGA_1001)</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable error message</summary>
    public required string Message { get; init; }

    /// <summary>Is this error retryable?</summary>
    public bool IsRetryable { get; init; }

    /// <summary>Original exception (if any)</summary>
    public Exception? Exception { get; init; }

    /// <summary>Additional context details</summary>
    public string? Details { get; init; }

    /// <summary>Create error from exception</summary>
    public static ErrorInfo FromException(Exception ex, string? code = null, bool isRetryable = false)
        => new()
        {
            Code = code ?? ErrorCodes.UnknownError,
            Message = ex.Message,
            IsRetryable = isRetryable,
            Exception = ex
        };

    /// <summary>Create validation error</summary>
    public static ErrorInfo Validation(string message, string? details = null)
        => new()
        {
            Code = ErrorCodes.ValidationFailed,
            Message = message,
            IsRetryable = false,
            Details = details
        };

    /// <summary>Create timeout error</summary>
    public static ErrorInfo Timeout(string message)
        => new()
        {
            Code = ErrorCodes.MessageTimeout,
            Message = message,
            IsRetryable = true
        };

    /// <summary>Create not found error</summary>
    public static ErrorInfo NotFound(string message)
        => new()
        {
            Code = ErrorCodes.HandlerNotFound,
            Message = message,
            IsRetryable = false
        };

    /// <summary>Create configuration error</summary>
    public static ErrorInfo Configuration(string message)
        => new()
        {
            Code = ErrorCodes.InvalidConfiguration,
            Message = message,
            IsRetryable = false
        };
}

