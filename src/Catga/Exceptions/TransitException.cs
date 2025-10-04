namespace Catga.Exceptions;

/// <summary>
/// Base exception for transit operations (100% AOT-compatible)
/// </summary>
public class TransitException : Exception
{
    public string? ErrorCode { get; init; }
    public bool IsRetryable { get; init; }
    public Dictionary<string, string>? Details { get; init; }

    public TransitException(string message, string? errorCode = null, bool isRetryable = false)
        : base(message)
    {
        ErrorCode = errorCode;
        IsRetryable = isRetryable;
    }

    public TransitException(string message, Exception innerException, string? errorCode = null, bool isRetryable = false)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        IsRetryable = isRetryable;
    }
}

/// <summary>
/// Exception for timeout scenarios
/// </summary>
public class TransitTimeoutException : TransitException
{
    public TransitTimeoutException(string message)
        : base(message, "TIMEOUT", isRetryable: true)
    {
    }
}

/// <summary>
/// Exception for validation failures
/// </summary>
public class TransitValidationException : TransitException
{
    public List<string> ValidationErrors { get; init; } = new();

    public TransitValidationException(string message, List<string> validationErrors)
        : base(message, "VALIDATION_FAILED", isRetryable: false)
    {
        ValidationErrors = validationErrors;
    }
}

/// <summary>
/// Exception for handler not found
/// </summary>
public class HandlerNotFoundException : TransitException
{
    public HandlerNotFoundException(string messageType)
        : base($"No handler found for message type: {messageType}", "HANDLER_NOT_FOUND", isRetryable: false)
    {
    }
}
