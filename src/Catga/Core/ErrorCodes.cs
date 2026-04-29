namespace Catga.Core;

/// <summary>
/// Catga unified error codes. All error codes in the framework use these constants.
/// </summary>
public static class ErrorCodes
{
    // ── Core ──────────────────────────────────────────────────────────────────
    public const string ValidationFailed    = "VALIDATION_FAILED";
    public const string HandlerFailed       = "HANDLER_FAILED";
    public const string HandlerNotFound     = "HANDLER_NOT_FOUND";
    public const string PipelineFailed      = "PIPELINE_FAILED";
    public const string PersistenceFailed   = "PERSISTENCE_FAILED";
    public const string LockFailed          = "LOCK_FAILED";
    public const string TransportFailed     = "TRANSPORT_FAILED";
    public const string SerializationFailed = "SERIALIZATION_FAILED";
    public const string Timeout             = "TIMEOUT";
    public const string Cancelled           = "CANCELLED";
    public const string InternalError       = "INTERNAL_ERROR";

    // ── HTTP / Domain ─────────────────────────────────────────────────────────
    public const string NotFound            = "NOT_FOUND";
    public const string Conflict            = "CONFLICT";
    public const string Unauthorized        = "UNAUTHORIZED";
    public const string Forbidden           = "FORBIDDEN";

    // ── Flow DSL ──────────────────────────────────────────────────────────────
    public const string FlowFailed          = "FLOW_FAILED";
    public const string FlowCancelled       = "FLOW_CANCELLED";
    public const string FlowTimeout         = "FLOW_TIMEOUT";
    public const string FlowCompensating    = "FLOW_COMPENSATING";
}

/// <summary>
/// Structured error information - zero-allocation struct.
/// </summary>
public readonly struct ErrorInfo
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public bool IsRetryable { get; init; }
    public Exception? Exception { get; init; }
    public string? Details { get; init; }

    public static ErrorInfo FromException(Exception ex, string? code = null, bool isRetryable = false)
        => new() { Code = code ?? ErrorCodes.InternalError, Message = ex.Message, IsRetryable = isRetryable, Exception = ex };

    public static ErrorInfo Validation(string message, string? details = null)
        => new() { Code = ErrorCodes.ValidationFailed, Message = message, IsRetryable = false, Details = details };

    public static ErrorInfo Timeout(string message)
        => new() { Code = ErrorCodes.Timeout, Message = message, IsRetryable = true };

    public static ErrorInfo NotFound(string message)
        => new() { Code = ErrorCodes.NotFound, Message = message, IsRetryable = false };
}
