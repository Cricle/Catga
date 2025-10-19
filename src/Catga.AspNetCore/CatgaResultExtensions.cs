using Catga.Core;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>CatgaResult to ASP.NET Core IResult conversion</summary>
public static class CatgaResultExtensions
{
    private const string HttpStatusCodeKey = "HttpStatusCode";
    private const string ErrorTypeKey = "ErrorType";

    public static IResult ToHttpResult<T>(this CatgaResult<T> result)
    {
        if (result.IsSuccess)
            return result.Value is null ? Results.NoContent() : Results.Ok(result.Value);

        var error = result.Error ?? "An error occurred";

        if (result.Metadata?.TryGetValue(HttpStatusCodeKey, out var statusCodeStr) == true)
        {
            if (int.TryParse(statusCodeStr, out var statusCode))
                return Results.Problem(detail: error, statusCode: statusCode);
        }

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

        return Results.BadRequest(new { error, metadata = result.Metadata?.GetAll() });
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

/// <summary>CatgaResult factory extensions with HTTP status hints</summary>
public static class CatgaResultHttpExtensions
{
    public static CatgaResult<T> NotFound<T>(string error)
    {
        var metadata = new ResultMetadata();
        metadata.Add("ErrorType", "NotFound");
        return new CatgaResult<T> { IsSuccess = false, Error = error, Metadata = metadata };
    }

    public static CatgaResult<T> Conflict<T>(string error)
    {
        var metadata = new ResultMetadata();
        metadata.Add("ErrorType", "Conflict");
        return new CatgaResult<T> { IsSuccess = false, Error = error, Metadata = metadata };
    }

    public static CatgaResult<T> ValidationError<T>(string error)
    {
        var metadata = new ResultMetadata();
        metadata.Add("ErrorType", "Validation");
        return new CatgaResult<T> { IsSuccess = false, Error = error, Metadata = metadata };
    }

    public static CatgaResult<T> Unauthorized<T>(string error)
    {
        var metadata = new ResultMetadata();
        metadata.Add("ErrorType", "Unauthorized");
        return new CatgaResult<T> { IsSuccess = false, Error = error, Metadata = metadata };
    }

    public static CatgaResult<T> Forbidden<T>(string error)
    {
        var metadata = new ResultMetadata();
        metadata.Add("ErrorType", "Forbidden");
        return new CatgaResult<T> { IsSuccess = false, Error = error, Metadata = metadata };
    }

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

