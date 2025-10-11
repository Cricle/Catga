using Catga.Results;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// CatgaResult extensions for ASP.NET Core IResult conversion
/// </summary>
public static class CatgaResultExtensions
{
    /// <summary>
    /// Convert CatgaResult to IResult with appropriate HTTP status codes
    /// </summary>
    public static IResult ToHttpResult<T>(this CatgaResult<T> result)
    {
        if (result.IsSuccess)
        {
            return result.Value is null
                ? Results.NoContent()
                : Results.Ok(result.Value);
        }

        // Map error messages to appropriate status codes
        var error = result.Error ?? "An error occurred";
        
        return error switch
        {
            string err when err.Contains("not found", StringComparison.OrdinalIgnoreCase)
                => Results.NotFound(new { error = err, metadata = result.Metadata }),
            
            string err when err.Contains("already", StringComparison.OrdinalIgnoreCase)
                => Results.Conflict(new { error = err, metadata = result.Metadata }),
            
            string err when err.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
                           err.Contains("must be", StringComparison.OrdinalIgnoreCase)
                => Results.UnprocessableEntity(new { error = err, metadata = result.Metadata }),
            
            string err when err.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                           err.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
                => Results.Forbid(),
            
            _ => Results.BadRequest(new { error = error, metadata = result.Metadata })
        };
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

