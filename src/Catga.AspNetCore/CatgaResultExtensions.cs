using Catga.Core;
using Microsoft.AspNetCore.Http;
using System.Diagnostics.CodeAnalysis;

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

        return errorCode switch
        {
            ErrorCodes.ValidationFailed    => Results.UnprocessableEntity(new { error, errorCode }),
            ErrorCodes.Timeout             => Results.Problem(detail: error, statusCode: 408),
            ErrorCodes.Cancelled           => Results.Problem(detail: error, statusCode: 408),
            ErrorCodes.HandlerFailed       => Results.BadRequest(new { error, errorCode }),
            ErrorCodes.HandlerNotFound     => Results.NotFound(new { error, errorCode }),
            ErrorCodes.PipelineFailed      => Results.BadRequest(new { error, errorCode }),
            ErrorCodes.PersistenceFailed   => Results.Problem(detail: error, statusCode: 503),
            ErrorCodes.LockFailed          => Results.Problem(detail: error, statusCode: 503),
            ErrorCodes.TransportFailed     => Results.Problem(detail: error, statusCode: 503),
            ErrorCodes.SerializationFailed => Results.BadRequest(new { error, errorCode }),
            ErrorCodes.NotFound            => Results.NotFound(new { error, errorCode }),
            ErrorCodes.Conflict            => Results.Conflict(new { error, errorCode }),
            ErrorCodes.Unauthorized        => Results.Unauthorized(),
            ErrorCodes.Forbidden           => Results.Forbid(),
            _                              => Results.BadRequest(new { error, errorCode })
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
                _   => Results.StatusCode(successStatusCode)
            };
        }
        return result.ToHttpResult();
    }

    [RequiresUnreferencedCode("This API may perform reflection on supplied parameters which may be trimmed if not referenced directly.")]
    public static IResult ToCreatedResult<T>(this CatgaResult<T> result, string routeName, object? routeValues = null)
    {
        if (result.IsSuccess)
            return Results.CreatedAtRoute(routeName, routeValues, result.Value);
        return result.ToHttpResult();
    }
}

/// <summary>CatgaResult factory extensions for common HTTP/domain error scenarios</summary>
public static class CatgaResultHttpExtensions
{
    // Keep old constants as aliases for backward compatibility
    [Obsolete("Use ErrorCodes.NotFound")] public const string HttpNotFound    = ErrorCodes.NotFound;
    [Obsolete("Use ErrorCodes.Conflict")] public const string HttpConflict    = ErrorCodes.Conflict;
    [Obsolete("Use ErrorCodes.Unauthorized")] public const string HttpUnauthorized = ErrorCodes.Unauthorized;
    [Obsolete("Use ErrorCodes.Forbidden")] public const string HttpForbidden  = ErrorCodes.Forbidden;

    public static CatgaResult<T> NotFound<T>(string error)
        => new() { IsSuccess = false, Error = error, ErrorCode = ErrorCodes.NotFound };

    public static CatgaResult<T> Conflict<T>(string error)
        => new() { IsSuccess = false, Error = error, ErrorCode = ErrorCodes.Conflict };

    public static CatgaResult<T> ValidationError<T>(string error)
        => new() { IsSuccess = false, Error = error, ErrorCode = ErrorCodes.ValidationFailed };

    public static CatgaResult<T> Unauthorized<T>(string error)
        => new() { IsSuccess = false, Error = error, ErrorCode = ErrorCodes.Unauthorized };

    public static CatgaResult<T> Forbidden<T>(string error)
        => new() { IsSuccess = false, Error = error, ErrorCode = ErrorCodes.Forbidden };
}

/// <summary>Extended HTTP result conversion (delegates to ToHttpResult which now handles all codes)</summary>
public static class CatgaResultHttpConversionExtensions
{
    public static IResult ToHttpResultEx<T>(this CatgaResult<T> result) => result.ToHttpResult();
}
