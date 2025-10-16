using Catga.Debugger.Replay;
using Catga.Debugger.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Catga.Debugger.AspNetCore.Endpoints;

/// <summary>AOT-compatible Minimal API endpoints for debugger</summary>
public static class DebuggerEndpoints
{
    /// <summary>Map debugger API endpoints</summary>
    public static RouteGroupBuilder MapCatgaDebuggerApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/debug-api")
            .WithTags("Catga Debugger");

        // Get all flows
        group.MapGet("/flows", GetFlowsAsync)
            .WithName("GetFlows")
            .WithSummary("Get all active flows")
            .Produces<FlowsResponse>();

        // Get specific flow
        group.MapGet("/flows/{correlationId}", GetFlowAsync)
            .WithName("GetFlow")
            .WithSummary("Get specific flow by correlation ID")
            .Produces<FlowResponse>()
            .Produces(404);

        // Get event store stats
        group.MapGet("/stats", GetStatsAsync)
            .WithName("GetStats")
            .WithSummary("Get event store statistics")
            .Produces<StatsResponse>();

        // Get system replay
        group.MapPost("/replay/system", ReplaySystemAsync)
            .WithName("ReplaySystem")
            .WithSummary("Start system-wide replay")
            .Produces<SystemReplayResponse>();

        // Get flow replay
        group.MapPost("/replay/flow", ReplayFlowAsync)
            .WithName("ReplayFlow")
            .WithSummary("Start flow-level replay")
            .Produces<FlowReplayResponse>();

        return group;
    }

    // AOT-compatible typed handlers

    private static async Task<Ok<FlowsResponse>> GetFlowsAsync(
        IEventStore eventStore,
        CancellationToken ct)
    {
        var stats = await eventStore.GetStatsAsync(ct);
        var flows = new List<FlowInfo>();

        // Get recent flows (last hour)
        var events = await eventStore.GetEventsAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            ct);

        var groupedFlows = events
            .GroupBy(e => e.CorrelationId)
            .Select(g => new FlowInfo
            {
                CorrelationId = g.Key,
                StartTime = g.Min(e => e.Timestamp),
                EndTime = g.Max(e => e.Timestamp),
                EventCount = g.Count(),
                HasErrors = g.Any(e => e.Type == Models.EventType.ExceptionThrown)
            })
            .OrderByDescending(f => f.StartTime)
            .Take(100)
            .ToList();

        return TypedResults.Ok(new FlowsResponse
        {
            Flows = groupedFlows,
            TotalFlows = stats.TotalFlows,
            Timestamp = DateTime.UtcNow
        });
    }

    private static async Task<Results<Ok<FlowResponse>, NotFound>> GetFlowAsync(
        string correlationId,
        IEventStore eventStore,
        CancellationToken ct)
    {
        var events = await eventStore.GetEventsByCorrelationAsync(correlationId, ct);
        var eventList = events.ToList();

        if (eventList.Count == 0)
            return TypedResults.NotFound();

        return TypedResults.Ok(new FlowResponse
        {
            CorrelationId = correlationId,
            StartTime = eventList.Min(e => e.Timestamp),
            EndTime = eventList.Max(e => e.Timestamp),
            EventCount = eventList.Count,
            Events = eventList.Select(e => new EventInfo
            {
                Id = e.Id,
                Type = e.Type.ToString(),
                Timestamp = e.Timestamp,
                ServiceName = e.ServiceName ?? "Unknown"
            }).ToList()
        });
    }

    private static async Task<Ok<StatsResponse>> GetStatsAsync(
        IEventStore eventStore,
        CancellationToken ct)
    {
        var stats = await eventStore.GetStatsAsync(ct);

        return TypedResults.Ok(new StatsResponse
        {
            TotalEvents = stats.TotalEvents,
            TotalFlows = stats.TotalFlows,
            StorageSizeBytes = stats.StorageSizeBytes,
            OldestEvent = stats.OldestEvent,
            NewestEvent = stats.NewestEvent,
            Timestamp = DateTime.UtcNow
        });
    }

    private static async Task<Ok<SystemReplayResponse>> ReplaySystemAsync(
        SystemReplayRequest request,
        IReplayEngine replayEngine,
        CancellationToken ct)
    {
        var replay = await replayEngine.ReplaySystemAsync(
            request.StartTime,
            request.EndTime,
            request.Speed,
            ct);

        return TypedResults.Ok(new SystemReplayResponse
        {
            EventCount = replay.Timeline.Count,
            StartTime = replay.StartTime,
            EndTime = replay.EndTime,
            Speed = replay.Speed
        });
    }

    private static async Task<Ok<FlowReplayResponse>> ReplayFlowAsync(
        FlowReplayRequest request,
        IReplayEngine replayEngine,
        CancellationToken ct)
    {
        var replay = await replayEngine.ReplayFlowAsync(request.CorrelationId, ct);

        return TypedResults.Ok(new FlowReplayResponse
        {
            CorrelationId = request.CorrelationId,
            TotalSteps = replay.StateMachine.TotalSteps,
            CurrentStep = replay.StateMachine.CurrentIndex
        });
    }
}

// AOT-compatible response types

public sealed record FlowsResponse
{
    public required List<FlowInfo> Flows { get; init; }
    public required long TotalFlows { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record FlowInfo
{
    public required string CorrelationId { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required int EventCount { get; init; }
    public required bool HasErrors { get; init; }
}

public sealed record FlowResponse
{
    public required string CorrelationId { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required int EventCount { get; init; }
    public required List<EventInfo> Events { get; init; }
}

public sealed record EventInfo
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string ServiceName { get; init; }
}

public sealed record StatsResponse
{
    public required long TotalEvents { get; init; }
    public required long TotalFlows { get; init; }
    public required long StorageSizeBytes { get; init; }
    public required DateTime OldestEvent { get; init; }
    public required DateTime NewestEvent { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record SystemReplayRequest
{
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public double Speed { get; init; } = 1.0;
}

public sealed record SystemReplayResponse
{
    public required int EventCount { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required double Speed { get; init; }
}

public sealed record FlowReplayRequest
{
    public required string CorrelationId { get; init; }
}

public sealed record FlowReplayResponse
{
    public required string CorrelationId { get; init; }
    public required int TotalSteps { get; init; }
    public required int CurrentStep { get; init; }
}

