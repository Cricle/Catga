using Catga.AspNetCore.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Catga.AspNetCore.Extensions;

/// <summary>
/// Extension methods for CorrelationId middleware
/// </summary>
public static class CorrelationIdExtensions
{
    /// <summary>
    /// Add CorrelationId middleware to the pipeline
    /// This should be added early in the pipeline (before routing)
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.UseMiddleware<CorrelationIdMiddleware>();
}

