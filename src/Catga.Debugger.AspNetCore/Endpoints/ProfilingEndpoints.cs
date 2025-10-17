using Catga.Debugger.Profiling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Catga.Debugger.AspNetCore.Endpoints;

/// <summary>
/// API endpoints for performance profiling
/// </summary>
public static class ProfilingEndpoints
{
    public static RouteGroupBuilder MapProfilingEndpoints(this RouteGroupBuilder group)
    {
        // Get flame graph for a correlation ID
        group.MapGet("/profiling/flame-graph/{correlationId}", GetFlameGraphAsync)
            .WithName("GetFlameGraph")
            .WithSummary("Gets a flame graph for a specific correlation ID")
            .Produces<FlameGraph>()
            .Produces(404);

        // Get slow queries
        group.MapGet("/profiling/slow-queries", GetSlowQueriesAsync)
            .WithName("GetSlowQueries")
            .WithSummary("Gets slow queries exceeding threshold")
            .Produces<List<SlowQuery>>();

        // Get hot spots
        group.MapGet("/profiling/hot-spots", GetHotSpotsAsync)
            .WithName("GetHotSpots")
            .WithSummary("Gets performance hot spots")
            .Produces<List<HotSpot>>();

        // Get GC analysis
        group.MapGet("/profiling/gc-analysis", GetGcAnalysisAsync)
            .WithName("GetGcAnalysis")
            .WithSummary("Gets GC pressure analysis")
            .Produces<GcAnalysis>();

        return group;
    }

    private static async Task<Results<Ok<FlameGraph>, NotFound>> GetFlameGraphAsync(
        string correlationId,
        [FromQuery] string type,
        FlameGraphBuilder builder)
    {
        FlameGraph graph = type?.ToLower() switch
        {
            "memory" => await builder.BuildMemoryFlameGraphAsync(correlationId),
            _ => await builder.BuildCpuFlameGraphAsync(correlationId)
        };

        return TypedResults.Ok(graph);
    }

    private static async Task<Ok<List<SlowQuery>>> GetSlowQueriesAsync(
        [FromQuery] int? thresholdMs,
        [FromQuery] int? topN,
        PerformanceAnalyzer analyzer)
    {
        var threshold = TimeSpan.FromMilliseconds(thresholdMs ?? 1000);
        var queries = await analyzer.DetectSlowQueriesAsync(threshold, topN ?? 10);
        return TypedResults.Ok(queries);
    }

    private static async Task<Ok<List<HotSpot>>> GetHotSpotsAsync(
        [FromQuery] int? topN,
        PerformanceAnalyzer analyzer)
    {
        var hotSpots = await analyzer.IdentifyHotSpotsAsync(topN ?? 10);
        return TypedResults.Ok(hotSpots);
    }

    private static Ok<GcAnalysis> GetGcAnalysisAsync(PerformanceAnalyzer analyzer)
    {
        var analysis = analyzer.AnalyzeGcPressure();
        return TypedResults.Ok(analysis);
    }
}

