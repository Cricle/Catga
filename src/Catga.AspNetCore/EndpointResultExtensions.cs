using Catga.Abstractions;
using Catga.Core;
using Microsoft.AspNetCore.Http;

namespace Catga.AspNetCore;

/// <summary>
/// Extension methods for mapping Catga results to HTTP responses.
/// Provides fluent result mapping with automatic status code selection.
/// </summary>
public static class EndpointResultExtensions
{
    /// <summary>
    /// Map CatgaResult to IResult with automatic status code selection.
    /// </summary>
    public static IResult ToIResult<T>(
        this CatgaResult<T> result,
        int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return successStatusCode switch
            {
                StatusCodes.Status201Created => Results.Created("", result.Value),
                StatusCodes.Status202Accepted => Results.Accepted("", result.Value),
                StatusCodes.Status204NoContent => Results.NoContent(),
                _ => Results.Ok(result.Value)
            };
        }

        return MapErrorToResult(result.Error);
    }

    /// <summary>
    /// Map CatgaResult to IResult with location header for created resources.
    /// </summary>
    public static IResult ToCreatedResult<T>(
        this CatgaResult<T> result,
        Func<T, string> locationSelector)
    {
        if (!result.IsSuccess)
            return MapErrorToResult(result.Error);

        var location = locationSelector(result.Value);
        return Results.Created(location, result.Value);
    }

    /// <summary>
    /// Map CatgaResult to IResult with custom success response.
    /// </summary>
    public static IResult ToIResult<T>(
        this CatgaResult<T> result,
        Func<T, IResult> successSelector)
    {
        if (result.IsSuccess)
            return successSelector(result.Value);

        return MapErrorToResult(result.Error);
    }

    /// <summary>
    /// Map CatgaResult (no response) to IResult.
    /// </summary>
    public static IResult ToIResult(
        this CatgaResult result,
        int successStatusCode = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return successStatusCode switch
            {
                StatusCodes.Status204NoContent => Results.NoContent(),
                StatusCodes.Status202Accepted => Results.Accepted(),
                _ => Results.Ok()
            };
        }

        return MapErrorToResult(result.Error);
    }

    /// <summary>
    /// Map error message to appropriate HTTP status code.
    /// </summary>
    private static IResult MapErrorToResult(string? error)
    {
        if (string.IsNullOrEmpty(error))
            return Results.BadRequest(new { error = "Unknown error" });

        return error switch
        {
            var e when e.Contains("NotFound", StringComparison.OrdinalIgnoreCase)
                => Results.NotFound(new { error }),

            var e when e.Contains("Conflict", StringComparison.OrdinalIgnoreCase)
                => Results.Conflict(new { error }),

            var e when e.Contains("Validation", StringComparison.OrdinalIgnoreCase)
                => Results.ValidationProblem(new Dictionary<string, string[]> { ["error"] = [error] }),

            var e when e.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                => Results.Unauthorized(),

            var e when e.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
                => Results.Forbid(),

            var e when e.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
                => Results.StatusCode(StatusCodes.Status504GatewayTimeout),

            var e when e.Contains("Unavailable", StringComparison.OrdinalIgnoreCase)
                => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),

            _ => Results.BadRequest(new { error })
        };
    }

    /// <summary>
    /// Chain multiple result mappings.
    /// </summary>
    public static async Task<IResult> ChainAsync<T>(
        this Task<CatgaResult<T>> resultTask,
        Func<T, Task<IResult>> nextHandler)
    {
        var result = await resultTask;

        if (!result.IsSuccess)
            return MapErrorToResult(result.Error);

        return await nextHandler(result.Value);
    }

    /// <summary>
    /// Transform result value before mapping to IResult.
    /// </summary>
    public static IResult MapValue<T, TResponse>(
        this CatgaResult<T> result,
        Func<T, TResponse> transformer)
    {
        if (!result.IsSuccess)
            return MapErrorToResult(result.Error);

        var transformed = transformer(result.Value);
        return Results.Ok(transformed);
    }

    /// <summary>
    /// Filter result based on predicate.
    /// </summary>
    public static IResult Filter<T>(
        this CatgaResult<T> result,
        Func<T, bool> predicate,
        string notFoundMessage = "Resource not found")
    {
        if (!result.IsSuccess)
            return MapErrorToResult(result.Error);

        if (!predicate(result.Value))
            return Results.NotFound(new { error = notFoundMessage });

        return Results.Ok(result.Value);
    }
}

/// <summary>
/// Fluent result builder for complex response mapping.
/// </summary>
public class ResultBuilder<T>
{
    private readonly CatgaResult<T> _result;
    private Func<T, IResult>? _successHandler;
    private Func<string, IResult>? _errorHandler;

    public ResultBuilder(CatgaResult<T> result)
    {
        _result = result;
    }

    /// <summary>
    /// Handle success case.
    /// </summary>
    public ResultBuilder<T> OnSuccess(Func<T, IResult> handler)
    {
        _successHandler = handler;
        return this;
    }

    /// <summary>
    /// Handle error case.
    /// </summary>
    public ResultBuilder<T> OnError(Func<string, IResult> handler)
    {
        _errorHandler = handler;
        return this;
    }

    /// <summary>
    /// Build the final IResult.
    /// </summary>
    public IResult Build()
    {
        if (_result.IsSuccess)
        {
            if (_successHandler != null)
                return _successHandler(_result.Value);

            return Results.Ok(_result.Value);
        }

        if (_errorHandler != null)
            return _errorHandler(_result.Error);

        return EndpointResultExtensions.ToIResult(_result);
    }
}

/// <summary>
/// Extension method to create result builder.
/// </summary>
public static class ResultBuilderExtensions
{
    public static ResultBuilder<T> BuildResult<T>(this CatgaResult<T> result)
    {
        return new ResultBuilder<T>(result);
    }
}
