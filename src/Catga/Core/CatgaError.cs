namespace Catga.Results;

/// <summary>
/// Error category for categorizing different types of errors
/// </summary>
public enum ErrorCategory
{
    /// <summary>
    /// Business logic error (e.g., OrderNotFound, InsufficientStock)
    /// </summary>
    Business = 1,

    /// <summary>
    /// System error (e.g., DatabaseTimeout, NetworkError)
    /// </summary>
    System = 2,

    /// <summary>
    /// Validation error (e.g., InvalidInput, MissingRequiredField)
    /// </summary>
    Validation = 3,

    /// <summary>
    /// Authorization/Authentication error
    /// </summary>
    Authorization = 4,

    /// <summary>
    /// Not found error
    /// </summary>
    NotFound = 5
}

/// <summary>
/// Detailed error information with code, message, and category
/// </summary>
public sealed record CatgaError
{
    /// <summary>
    /// Error code (e.g., "ORD_001", "SYS_2001")
    /// </summary>
    public string Code { get; init; }

    /// <summary>
    /// User-friendly error message
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// Technical details (optional, for debugging)
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Error category
    /// </summary>
    public ErrorCategory Category { get; init; }

    /// <summary>
    /// Additional metadata (optional)
    /// </summary>
    public IDictionary<string, object>? Metadata { get; init; }

    public CatgaError(string code, string message, ErrorCategory category = ErrorCategory.Business)
    {
        Code = code;
        Message = message;
        Category = category;
    }

    public CatgaError(string code, string message, string? details, ErrorCategory category = ErrorCategory.Business)
    {
        Code = code;
        Message = message;
        Details = details;
        Category = category;
    }

    /// <summary>
    /// Create a business error
    /// </summary>
    public static CatgaError Business(string code, string message, string? details = null) =>
        new(code, message, details, ErrorCategory.Business);

    /// <summary>
    /// Create a system error
    /// </summary>
    public static CatgaError System(string code, string message, string? details = null) =>
        new(code, message, details, ErrorCategory.System);

    /// <summary>
    /// Create a validation error
    /// </summary>
    public static CatgaError Validation(string code, string message, string? details = null) =>
        new(code, message, details, ErrorCategory.Validation);

    /// <summary>
    /// Create an authorization error
    /// </summary>
    public static CatgaError Authorization(string code, string message, string? details = null) =>
        new(code, message, details, ErrorCategory.Authorization);

    /// <summary>
    /// Create a not found error
    /// </summary>
    public static CatgaError NotFound(string code, string message, string? details = null) =>
        new(code, message, details, ErrorCategory.NotFound);

    /// <summary>
    /// Add metadata to the error
    /// </summary>
    public CatgaError WithMetadata(string key, object value)
    {
        var metadata = Metadata != null
            ? new Dictionary<string, object>(Metadata)
            : new Dictionary<string, object>();

        metadata[key] = value;

        return this with { Metadata = metadata };
    }
}

/// <summary>
/// Common error codes used across the framework
/// </summary>
public static class CatgaErrorCodes
{
    // Business errors (1xxx)
    public const string BusinessLogicError = "BIZ_1000";
    public const string ResourceNotFound = "BIZ_1001";
    public const string DuplicateResource = "BIZ_1002";
    public const string InvalidOperation = "BIZ_1003";

    // System errors (2xxx)
    public const string SystemError = "SYS_2000";
    public const string DatabaseError = "SYS_2001";
    public const string NetworkError = "SYS_2002";
    public const string TimeoutError = "SYS_2003";
    public const string CircuitBreakerOpen = "SYS_2004";
    public const string RateLimitExceeded = "SYS_2005";
    public const string ConcurrencyLimitExceeded = "SYS_2006";

    // Validation errors (3xxx)
    public const string ValidationError = "VAL_3000";
    public const string InvalidInput = "VAL_3001";
    public const string MissingRequiredField = "VAL_3002";
    public const string InvalidFormat = "VAL_3003";

    // Authorization errors (4xxx)
    public const string Unauthorized = "AUTH_4001";
    public const string Forbidden = "AUTH_4002";
}

