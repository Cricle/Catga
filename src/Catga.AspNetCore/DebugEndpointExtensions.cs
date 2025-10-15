using System.Diagnostics.CodeAnalysis;
using Catga.Debugging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Debug endpoint extensions - expose debug info via HTTP
/// </summary>
public static class CatgaDebugEndpointExtensions
{
    /// <summary>
    /// Map Catga debug endpoints - /debug/flows, /debug/stats
    /// </summary>
    /// <remarks>
    /// Debug endpoints use reflection-based JSON serialization and are intended for development only.
    /// Do not call this method in AOT-published applications.
    /// </remarks>
    [RequiresUnreferencedCode("Debug endpoints use reflection-based JSON serialization. Not compatible with trimming.")]
    [RequiresDynamicCode("Debug endpoints require runtime code generation. Not compatible with Native AOT.")]
    public static IEndpointRouteBuilder MapCatgaDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var debugGroup = endpoints.MapGroup("/debug").WithTags("Debug");

        // List active flows
        debugGroup.MapGet("/flows", (MessageFlowTracker? tracker) =>
        {
            if (tracker == null)
                return Results.Json(new { error = "Debug not enabled. Call .WithDebug()" });

            var flows = tracker.GetActiveFlows();
            return Results.Json(new
            {
                count = flows.Count,
                flows = flows.Select(f => new
                {
                    correlationId = f.CorrelationId,
                    messageType = f.MessageType,
                    duration = $"{f.TotalDuration.TotalMilliseconds:F1}ms",
                    steps = f.StepCount,
                    success = f.Success
                })
            });
        }).WithName("GetActiveFlows");

        // Get specific flow
        debugGroup.MapGet("/flows/{correlationId}", (string correlationId, MessageFlowTracker? tracker) =>
        {
            if (tracker == null)
                return Results.Json(new { error = "Debug not enabled" });

            var flow = tracker.GetFlow(correlationId);
            if (flow == null)
                return Results.NotFound(new { error = "Flow not found" });

            return Results.Json(flow);
        }).WithName("GetFlow");

        // Get statistics
        debugGroup.MapGet("/stats", (MessageFlowTracker? tracker) =>
        {
            if (tracker == null)
                return Results.Json(new { error = "Debug not enabled" });

            var stats = tracker.GetStatistics();
            return Results.Json(new
            {
                totalFlows = stats.TotalFlows,
                activeFlows = stats.ActiveFlows,
                memoryEstimate = $"{stats.MemoryEstimate / 1024}KB",
                poolInfo = stats.PooledContexts
            });
        }).WithName("GetDebugStats");

        return endpoints;
    }
}

