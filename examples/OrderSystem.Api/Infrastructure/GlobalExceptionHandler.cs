using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace OrderSystem.Api.Infrastructure;

/// <summary>
/// Global exception handler implementing IExceptionHandler (ASP.NET Core 8+).
/// Provides consistent error responses and logging for all unhandled exceptions.
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        logger.LogError(
            exception,
            "Unhandled exception occurred. TraceId: {TraceId}, Path: {Path}",
            traceId,
            httpContext.Request.Path);

        var problemDetails = exception switch
        {
            ArgumentException => CreateProblemDetails(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                exception.Message,
                traceId),

            KeyNotFoundException => CreateProblemDetails(
                StatusCodes.Status404NotFound,
                "Not Found",
                exception.Message,
                traceId),

            UnauthorizedAccessException => CreateProblemDetails(
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Access denied",
                traceId),

            InvalidOperationException => CreateProblemDetails(
                StatusCodes.Status409Conflict,
                "Conflict",
                exception.Message,
                traceId),

            OperationCanceledException => CreateProblemDetails(
                StatusCodes.Status499ClientClosedRequest,
                "Client Closed Request",
                "The request was cancelled",
                traceId),

            _ => CreateProblemDetails(
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                environment.IsDevelopment() ? exception.Message : "An unexpected error occurred",
                traceId)
        };

        httpContext.Response.StatusCode = problemDetails.Status ?? 500;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static ProblemDetails CreateProblemDetails(
        int statusCode,
        string title,
        string detail,
        string traceId)
    {
        return new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Extensions = { ["traceId"] = traceId }
        };
    }
}
