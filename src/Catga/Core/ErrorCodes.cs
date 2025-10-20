namespace Catga.Core;

/// <summary>
/// Catga core error codes - simple and focused
/// </summary>
public static class ErrorCodes
{
    /// <summary>Validation failed</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";
    
    /// <summary>Handler execution failed</summary>
    public const string HandlerFailed = "HANDLER_FAILED";
    
    /// <summary>Pipeline execution failed</summary>
    public const string PipelineFailed = "PIPELINE_FAILED";
    
    /// <summary>Inbox/Outbox persistence failed</summary>
    public const string PersistenceFailed = "PERSISTENCE_FAILED";
    
    /// <summary>Failed to acquire lock</summary>
    public const string LockFailed = "LOCK_FAILED";
    
    /// <summary>Message transport failed</summary>
    public const string TransportFailed = "TRANSPORT_FAILED";
    
    /// <summary>Serialization/Deserialization failed</summary>
    public const string SerializationFailed = "SERIALIZATION_FAILED";
    
    /// <summary>Operation timeout</summary>
    public const string Timeout = "TIMEOUT";
    
    /// <summary>Operation cancelled</summary>
    public const string Cancelled = "CANCELLED";
    
    /// <summary>Unknown/Internal error</summary>
    public const string InternalError = "INTERNAL_ERROR";
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
            Code = code ?? ErrorCodes.InternalError,
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
            Code = ErrorCodes.Timeout,
            Message = message,
            IsRetryable = true
        };
}