using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.AspNetCore;

/// <summary>
/// Interface for serializing error responses. Implement this for AOT-compatible serialization.
/// </summary>
public interface IErrorResponseSerializer
{
    string ContentType { get; }
    Task WriteAsync(HttpResponse response, ErrorResponse error);
}

/// <summary>
/// Default error response serializer using plain text format.
/// </summary>
public sealed class PlainTextErrorResponseSerializer : IErrorResponseSerializer
{
    public static readonly PlainTextErrorResponseSerializer Instance = new();

    public string ContentType => "text/plain";

    public Task WriteAsync(HttpResponse response, ErrorResponse error)
    {
        return response.WriteAsync($"Error: {error.Message}\nType: {error.Type}\nTimestamp: {error.Timestamp:O}");
    }
}

/// <summary>
/// Middleware for handling errors in Catga endpoint handlers.
/// Provides consistent error response formatting and logging.
/// </summary>
public class EndpointErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IErrorResponseSerializer _serializer;
    private readonly ILogger<EndpointErrorHandlingMiddleware> _logger;

    public EndpointErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<EndpointErrorHandlingMiddleware> logger,
        IErrorResponseSerializer? serializer = null)
    {
        _next = next;
        _logger = logger;
        _serializer = serializer ?? PlainTextErrorResponseSerializer.Instance;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = _serializer.ContentType;

        var response = new ErrorResponse
        {
            Message = exception.Message,
            Type = exception.GetType().Name,
            Timestamp = DateTime.UtcNow,
            TraceId = context.TraceIdentifier
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

        await _serializer.WriteAsync(context.Response, response);
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

    /// <summary>
    /// Register a custom error response serializer.
    /// </summary>
    public static IServiceCollection AddErrorResponseSerializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, IErrorResponseSerializer
    {
        return services.AddSingleton<IErrorResponseSerializer, T>();
    }

    /// <summary>
    /// Register a custom error response serializer instance.
    /// </summary>
    public static IServiceCollection AddErrorResponseSerializer(this IServiceCollection services, IErrorResponseSerializer serializer)
    {
        return services.AddSingleton(serializer);
    }
}
