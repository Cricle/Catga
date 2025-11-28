namespace Catga.Exceptions;

/// <summary>Base exception for Catga (AOT-compatible)</summary>
public class CatgaException : Exception
{
    public string? ErrorCode { get; init; }
    public bool IsRetryable { get; init; }
    public Dictionary<string, string>? Details { get; init; }

    public CatgaException(string message, string? errorCode = null, bool isRetryable = false) : base(message)
    {
        ErrorCode = errorCode;
        IsRetryable = isRetryable;
    }

    public CatgaException(string message, Exception innerException, string? errorCode = null, bool isRetryable = false) : base(message, innerException)
    {
        ErrorCode = errorCode;
        IsRetryable = isRetryable;
    }
}

/// <summary>Timeout exception</summary>
public class CatgaTimeoutException : CatgaException
{
    public CatgaTimeoutException(string message) : base(message, "TIMEOUT", isRetryable: true) { }
}

/// <summary>Validation exception</summary>
public class CatgaValidationException : CatgaException
{
    public List<string> ValidationErrors { get; init; } = new();

    public CatgaValidationException(string message, List<string> validationErrors) : base(message, "VALIDATION_FAILED", isRetryable: false)
        => ValidationErrors = validationErrors;
}

/// <summary>Handler not found exception</summary>
public class HandlerNotFoundException : CatgaException
{
    public HandlerNotFoundException(string messageType) : base($"No handler found for message type: {messageType}", "HANDLER_NOT_FOUND", isRetryable: false) { }
}
