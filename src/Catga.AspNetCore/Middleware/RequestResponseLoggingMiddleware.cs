using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Catga.AspNetCore.Middleware;

/// <summary>
/// Middleware for logging HTTP requests and responses with timing information.
/// </summary>
public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;

    public RequestResponseLoggingMiddleware(RequestDelegate next, ILogger<RequestResponseLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var originalBodyStream = context.Response.Body;

        try
        {
            using (var memoryStream = new MemoryStream())
            {
                context.Response.Body = memoryStream;

                LogRequest(context);

                await _next(context);

                stopwatch.Stop();
                LogResponse(context, stopwatch.ElapsedMilliseconds);

                await memoryStream.CopyToAsync(originalBodyStream);
            }
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private void LogRequest(HttpContext context)
    {
        var request = context.Request;
        _logger.LogInformation(
            "HTTP Request: {Method} {Path} | IP: {RemoteIP} | ContentType: {ContentType}",
            request.Method,
            request.Path,
            context.Connection.RemoteIpAddress,
            request.ContentType);
    }

    private void LogResponse(HttpContext context, long elapsedMilliseconds)
    {
        var response = context.Response;
        var logLevel = response.StatusCode >= 500 ? LogLevel.Error :
                      response.StatusCode >= 400 ? LogLevel.Warning :
                      LogLevel.Information;

        _logger.Log(
            logLevel,
            "HTTP Response: {StatusCode} | Duration: {ElapsedMs}ms | Path: {Path}",
            response.StatusCode,
            elapsedMilliseconds,
            context.Request.Path);
    }
}

/// <summary>
/// Extension methods for RequestResponseLoggingMiddleware.
/// </summary>
public static class RequestResponseLoggingExtensions
{
    public static IApplicationBuilder UseRequestResponseLogging(this IApplicationBuilder app)
        => app.UseMiddleware<RequestResponseLoggingMiddleware>();
}
