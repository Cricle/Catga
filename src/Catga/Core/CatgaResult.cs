using Catga.Exceptions;

namespace Catga.Core;

/// <summary>Catga operation result with value (zero-allocation struct for performance)</summary>
public record struct CatgaResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }
    public CatgaException? Exception { get; init; }

    /// <summary>Error code (e.g., CATGA_1001)</summary>
    public string? ErrorCode { get; init; }

    public static CatgaResult<T> Success(T value)
        => new() { IsSuccess = true, Value = value };

    public static CatgaResult<T> Failure(string error, CatgaException? exception = null)
        => new() { IsSuccess = false, Error = error, Exception = exception, ErrorCode = exception?.ErrorCode };

    /// <summary>Create failure result from ErrorInfo (zero exception allocation)</summary>
    public static CatgaResult<T> Failure(ErrorInfo errorInfo)
        => new()
        {
            IsSuccess = false,
            Error = errorInfo.Message,
            ErrorCode = errorInfo.Code,
            Exception = errorInfo.Exception as CatgaException
        };
}

/// <summary>Catga operation result without value (zero-allocation struct for performance)</summary>
public record struct CatgaResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public CatgaException? Exception { get; init; }

    /// <summary>Error code (e.g., CATGA_1001)</summary>
    public string? ErrorCode { get; init; }

    public static CatgaResult Success()
        => new() { IsSuccess = true };

    public static CatgaResult Failure(string error, CatgaException? exception = null)
        => new() { IsSuccess = false, Error = error, Exception = exception, ErrorCode = exception?.ErrorCode };

    /// <summary>Create failure result from ErrorInfo (zero exception allocation)</summary>
    public static CatgaResult Failure(ErrorInfo errorInfo)
        => new()
        {
            IsSuccess = false,
            Error = errorInfo.Message,
            ErrorCode = errorInfo.Code,
            Exception = errorInfo.Exception as CatgaException
        };
}
