using Catga.Results;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// CatgaResult extensions for ASP.NET Core IResult conversion
/// </summary>
public static class CatgaResultExtensions
{
    // Metadata keys for HTTP status code mapping
    private const string HttpStatusCodeKey = "HttpStatusCode";
    private const string ErrorTypeKey = "ErrorType";

    /// <summary>
    /// Convert CatgaResult to IResult with appropriate HTTP status codes
    /// Uses Metadata["HttpStatusCode"] or Metadata["ErrorType"] for explicit mapping
    /// </summary>
    public static IResult ToHttpResult<T>(this CatgaResult<T> result)
    {
        if (result.IsSuccess)
        {
            return result.Value is null
                ? Results.NoContent()
                : Results.Ok(result.Value);
        }

        var error = result.Error ?? "An error occurred";
        
        // Check for explicit HTTP status code in metadata
        if (result.Metadata?.TryGetValue(HttpStatusCodeKey, out var statusCodeStr) == true)
        {
            if (int.TryParse(statusCodeStr, out var statusCode))
            {
                return Results.Problem(
                    detail: error,
                    statusCode: statusCode);
            }
        }

        // Check for error type in metadata
        if (result.Metadata?.TryGetValue(ErrorTypeKey, out var errorType) == true)
        {
            return errorType switch
            {
                "NotFound" => Results.NotFound(new { error, metadata = result.Metadata?.GetAll() }),
                "Conflict" => Results.Conflict(new { error, metadata = result.Metadata?.GetAll() }),
                "Validation" => Results.UnprocessableEntity(new { error, metadata = result.Metadata?.GetAll() }),
                "Unauthorized" => Results.Unauthorized(),
                "Forbidden" => Results.Forbid(),
                _ => Results.BadRequest(new { error, metadata = result.Metadata?.GetAll() })
            };
        }

        // Default: BadRequest
        return Results.BadRequest(new { error, metadata = result.Metadata?.GetAll() });
    }

    /// <summary>
    /// Convert CatgaResult to IResult with custom success status code
    /// </summary>
    public static IResult ToHttpResult<T>(this CatgaResult<T> result, int successStatusCode)
    {
        if (result.IsSuccess)
        {
            return successStatusCode switch
            {
                201 => Results.Created(string.Empty, result.Value),
                202 => Results.Accepted(string.Empty, result.Value),
                204 => Results.NoContent(),
                _ => Results.StatusCode(successStatusCode)
            };
        }

        return result.ToHttpResult();
    }

    /// <summary>
    /// Convert CatgaResult to CreatedAtRoute result
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("This API may perform reflection on supplied parameters which may be trimmed if not referenced directly.")]
    public static IResult ToCreatedResult<T>(
        this CatgaResult<T> result,
        string routeName,
        object? routeValues = null)
    {
        if (result.IsSuccess)
        {
            return Results.CreatedAtRoute(routeName, routeValues, result.Value);
        }

        return result.ToHttpResult();
    }
}

/// <summary>
/// CatgaResult factory extensions for creating results with HTTP status code hints
/// </summary>
public static class CatgaResultHttpExtensions
{
    /// <summary>
    /// Create a NotFound failure result
    /// </summary>
    public static CatgaResult<T> NotFound<T>(string error)
    {
        var metadata = new ResultMetadata();
        metadata.Add("ErrorType", "NotFound");
        return new CatgaResult<T>
        {
            IsSuccess = false,
            Error = error,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Create a Conflict failure result
    /// </summary>
    public static CatgaResult<T> Conflict<T>(string error)
    {
        var metadata = new ResultMetadata();
        metadata.Add("ErrorType", "Conflict");
        return new CatgaResult<T>
        {
            IsSuccess = false,
            Error = error,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Create a Validation failure result
    /// </summary>
    public static CatgaResult<T> ValidationError<T>(string error)
    {
        var metadata = new ResultMetadata();
        metadata.Add("ErrorType", "Validation");
        return new CatgaResult<T>
        {
            IsSuccess = false,
            Error = error,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Create an Unauthorized failure result
    /// </summary>
    public static CatgaResult<T> Unauthorized<T>(string error)
    {
        var metadata = new ResultMetadata();
        metadata.Add("ErrorType", "Unauthorized");
        return new CatgaResult<T>
        {
            IsSuccess = false,
            Error = error,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Create a Forbidden failure result
    /// </summary>
    public static CatgaResult<T> Forbidden<T>(string error)
    {
        var metadata = new ResultMetadata();
        metadata.Add("ErrorType", "Forbidden");
        return new CatgaResult<T>
        {
            IsSuccess = false,
            Error = error,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Create a failure result with custom HTTP status code
    /// </summary>
    public static CatgaResult<T> WithStatusCode<T>(this CatgaResult<T> result, int statusCode)
    {
        var metadata = result.Metadata ?? new ResultMetadata();
        metadata.Add("HttpStatusCode", statusCode.ToString());
        
        return new CatgaResult<T>
        {
            IsSuccess = result.IsSuccess,
            Value = result.Value,
            Error = result.Error,
            Exception = result.Exception,
            Metadata = metadata
        };
    }
}

