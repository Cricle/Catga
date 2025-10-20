using Catga.Core;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>CatgaResult to ASP.NET Core IResult conversion</summary>
public static class CatgaResultExtensions
{
    public static IResult ToHttpResult<T>(this CatgaResult<T> result)
    {
        if (result.IsSuccess)
            return result.Value is null ? Results.NoContent() : Results.Ok(result.Value);

        var error = result.Error ?? "An error occurred";
        var errorCode = result.ErrorCode;

        // Map Catga error codes to HTTP status codes
        return errorCode switch
        {
            ErrorCodes.ValidationFailed => Results.UnprocessableEntity(new { error, errorCode }),
            ErrorCodes.Timeout => Results.Problem(detail: error, statusCode: 408),
            ErrorCodes.Cancelled => Results.Problem(detail: error, statusCode: 408),
            ErrorCodes.HandlerFailed => Results.BadRequest(new { error, errorCode }),
            ErrorCodes.PipelineFailed => Results.BadRequest(new { error, errorCode }),
            ErrorCodes.PersistenceFailed => Results.Problem(detail: error, statusCode: 503),
            ErrorCodes.LockFailed => Results.Problem(detail: error, statusCode: 503),
            ErrorCodes.TransportFailed => Results.Problem(detail: error, statusCode: 503),
            ErrorCodes.SerializationFailed => Results.BadRequest(new { error, errorCode }),
            _ => Results.BadRequest(new { error, errorCode })
        };
    }

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

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("This API may perform reflection on supplied parameters which may be trimmed if not referenced directly.")]
    public static IResult ToCreatedResult<T>(this CatgaResult<T> result, string routeName, object? routeValues = null)
    {
        if (result.IsSuccess)
            return Results.CreatedAtRoute(routeName, routeValues, result.Value);
        return result.ToHttpResult();
    }
}

/// <summary>CatgaResult factory extensions with HTTP status code hints</summary>
/// <remarks>
/// These are custom error codes outside the core Catga error codes.
/// They map to HTTP status codes for ASP.NET Core convenience.
/// </remarks>
public static class CatgaResultHttpExtensions
{
    // Custom HTTP error codes (not in core ErrorCodes)
    public const string HttpNotFound = "HTTP_NOT_FOUND";
    public const string HttpConflict = "HTTP_CONFLICT";
    public const string HttpUnauthorized = "HTTP_UNAUTHORIZED";
    public const string HttpForbidden = "HTTP_FORBIDDEN";

    public static CatgaResult<T> NotFound<T>(string error)
        => new() { IsSuccess = false, Error = error, ErrorCode = HttpNotFound };

    public static CatgaResult<T> Conflict<T>(string error)
        => new() { IsSuccess = false, Error = error, ErrorCode = HttpConflict };

    public static CatgaResult<T> ValidationError<T>(string error)
        => new() { IsSuccess = false, Error = error, ErrorCode = ErrorCodes.ValidationFailed };

    public static CatgaResult<T> Unauthorized<T>(string error)
        => new() { IsSuccess = false, Error = error, ErrorCode = HttpUnauthorized };

    public static CatgaResult<T> Forbidden<T>(string error)
        => new() { IsSuccess = false, Error = error, ErrorCode = HttpForbidden };
}

/// <summary>Extended HTTP result conversion with custom HTTP error codes</summary>
public static class CatgaResultHttpConversionExtensions
{
    public static IResult ToHttpResultEx<T>(this CatgaResult<T> result)
    {
        if (result.IsSuccess)
            return result.Value is null ? Results.NoContent() : Results.Ok(result.Value);

        var error = result.Error ?? "An error occurred";
        var errorCode = result.ErrorCode;

        // Map all error codes (core + HTTP custom) to HTTP status codes
        return errorCode switch
        {
            // Custom HTTP error codes
            CatgaResultHttpExtensions.HttpNotFound => Results.NotFound(new { error, errorCode }),
            CatgaResultHttpExtensions.HttpConflict => Results.Conflict(new { error, errorCode }),
            CatgaResultHttpExtensions.HttpUnauthorized => Results.Unauthorized(),
            CatgaResultHttpExtensions.HttpForbidden => Results.Forbid(),

            // Core Catga error codes
            ErrorCodes.ValidationFailed => Results.UnprocessableEntity(new { error, errorCode }),
            ErrorCodes.Timeout => Results.Problem(detail: error, statusCode: 408),
            ErrorCodes.Cancelled => Results.Problem(detail: error, statusCode: 408),
            ErrorCodes.HandlerFailed => Results.BadRequest(new { error, errorCode }),
            ErrorCodes.PipelineFailed => Results.BadRequest(new { error, errorCode }),
            ErrorCodes.PersistenceFailed => Results.Problem(detail: error, statusCode: 503),
            ErrorCodes.LockFailed => Results.Problem(detail: error, statusCode: 503),
            ErrorCodes.TransportFailed => Results.Problem(detail: error, statusCode: 503),
            ErrorCodes.SerializationFailed => Results.BadRequest(new { error, errorCode }),

            _ => Results.BadRequest(new { error, errorCode })
        };
    }
}

