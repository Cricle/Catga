using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Catga.AspNetCore;

/// <summary>
/// Middleware for handling errors in Catga endpoint handlers.
/// Provides consistent error response formatting and logging.
/// </summary>
public class EndpointErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<EndpointErrorHandlingMiddleware> _logger;

    public EndpointErrorHandlingMiddleware(RequestDelegate next, ILogger<EndpointErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = new ErrorResponse
        {
            Message = exception.Message,
            Type = exception.GetType().Name,
            Timestamp = DateTime.UtcNow
        };

        switch (exception)
        {
            case ArgumentNullException:
            case ArgumentException:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Message = "Invalid request parameters";
                break;

            case InvalidOperationException:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Message = "Invalid operation";
                break;

            case KeyNotFoundException:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Message = "Resource not found";
                break;

            case UnauthorizedAccessException:
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                response.Message = "Unauthorized access";
                break;

            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.Message = "An unexpected error occurred";
                break;
        }

#pragma warning disable IL2026, IL3050 // AOT: ErrorResponse is a simple POCO with known properties
        return context.Response.WriteAsJsonAsync(response);
#pragma warning restore IL2026, IL3050
    }
}

/// <summary>
/// Standard error response format.
/// </summary>
public class ErrorResponse
{
    public required string Message { get; set; }
    public string? Type { get; set; }
    public DateTime Timestamp { get; set; }
    public string? TraceId { get; set; }
}

/// <summary>
/// Extension methods for error handling middleware.
/// </summary>
public static class ErrorHandlingMiddlewareExtensions
{
    /// <summary>
    /// Add error handling middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseEndpointErrorHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<EndpointErrorHandlingMiddleware>();
    }
}
