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

/// <summary>Configuration exception</summary>
public class CatgaConfigurationException : CatgaException
{
    public CatgaConfigurationException(string message) : base(message, "CONFIGURATION_ERROR", isRetryable: false) { }

    /// <summary>
    /// Create exception for missing serializer registration
    /// </summary>
    public static CatgaConfigurationException SerializerNotRegistered()
    {
        return new CatgaConfigurationException(
            "❌ IMessageSerializer is not registered.\n\n" +
            "Quick Fix:\n" +
            "  services.AddCatga().UseMemoryPack();  // Recommended for AOT\n\n" +
            "Or manually:\n" +
            "  services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();\n\n" +
            "Available serializers:\n" +
            "  ✅ MemoryPack (recommended for AOT): Install Catga.Serialization.MemoryPack\n" +
            "     - 100% AOT compatible, 5x faster, 40% smaller\n" +
            "  ⚠️ JSON: Install Catga.Serialization.Json\n" +
            "     - Requires JsonSerializerContext for AOT\n\n" +
            "Learn more: https://github.com/catga/docs/serialization");
    }
}
