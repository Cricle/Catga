using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Catga.AspNetCore.Middleware;

/// <summary>
/// Rate limiting middleware using sliding window algorithm.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly RateLimitOptions _options;
    private readonly ConcurrentDictionary<string, RateLimitBucket> _buckets;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger, RateLimitOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
        _buckets = new ConcurrentDictionary<string, RateLimitBucket>();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var bucket = _buckets.GetOrAdd(clientId, _ => new RateLimitBucket(_options.WindowSize));

        if (!bucket.IsAllowed(_options.RequestsPerWindow))
        {
            _logger.LogWarning("Rate limit exceeded for client: {ClientId}", clientId);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.Add("Retry-After", _options.WindowSize.TotalSeconds.ToString());
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
            return;
        }

        await _next(context);
    }

    private string GetClientIdentifier(HttpContext context)
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userId = context.User?.FindFirst("sub")?.Value ?? "anonymous";
        return $"{clientIp}:{userId}";
    }
}

public class RateLimitOptions
{
    public int RequestsPerWindow { get; set; } = 100;
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromMinutes(1);
}

public class RateLimitBucket
{
    private readonly TimeSpan _windowSize;
    private readonly Queue<DateTime> _requests = new();
    private readonly object _lock = new();

    public RateLimitBucket(TimeSpan windowSize)
    {
        _windowSize = windowSize;
    }

    public bool IsAllowed(int maxRequests)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var windowStart = now - _windowSize;

            while (_requests.Count > 0 && _requests.Peek() < windowStart)
            {
                _requests.Dequeue();
            }

            if (_requests.Count < maxRequests)
            {
                _requests.Enqueue(now);
                return true;
            }

            return false;
        }
    }
}

/// <summary>
/// Extension methods for RateLimitingMiddleware.
/// </summary>
public static class RateLimitingExtensions
{
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app, RateLimitOptions? options = null)
    {
        var opts = options ?? new RateLimitOptions();
        return app.UseMiddleware<RateLimitingMiddleware>(opts);
    }
}
