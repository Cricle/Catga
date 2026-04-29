using Catga.Core;

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

    public CatgaException(string message, Exception innerException, string? errorCode = null, bool isRetryable = false)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        IsRetryable = isRetryable;
    }
}

public class CatgaTimeoutException : CatgaException
{
    public CatgaTimeoutException(string message)
        : base(message, ErrorCodes.Timeout, isRetryable: true) { }
}

public class CatgaValidationException : CatgaException
{
    public List<string> ValidationErrors { get; init; } = new();

    public CatgaValidationException(string message, List<string> validationErrors)
        : base(message, ErrorCodes.ValidationFailed, isRetryable: false)
        => ValidationErrors = validationErrors;
}

public class HandlerNotFoundException : CatgaException
{
    public HandlerNotFoundException(string messageType)
        : base($"No handler found for message type: {messageType}", ErrorCodes.HandlerNotFound, isRetryable: false) { }
}
