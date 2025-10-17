using Microsoft.AspNetCore.Http;
using System.Threading;

namespace Catga.AspNetCore.Middleware;

/// <summary>
/// Middleware to manage global CorrelationId for the entire HTTP request
/// Ensures all commands/events in a single request share the same CorrelationId
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    // AsyncLocal to store CorrelationId for the current async context
    private static readonly AsyncLocal<string?> _correlationId = new();

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Get the current CorrelationId for this request context
    /// </summary>
    public static string? Current => _correlationId.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        // Try to get CorrelationId from request header
        string? correlationId = null;
        if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var headerValue))
        {
            correlationId = headerValue.FirstOrDefault();
        }

        // Generate new CorrelationId if not provided
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        // Store in AsyncLocal for access by any code in this request
        _correlationId.Value = correlationId;

        // Add to HttpContext.Items for easier access
        context.Items["CorrelationId"] = correlationId;

        // Add to response header
        context.Response.Headers.TryAdd(CorrelationIdHeaderName, correlationId);

        try
        {
            await _next(context);
        }
        finally
        {
            // Clean up
            _correlationId.Value = null;
        }
    }
}

