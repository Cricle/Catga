using Catga.Debugger.Models;
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

        // Get all events
        group.MapGet("/events", GetEventsAsync)
            .WithName("GetEvents")
            .WithSummary("Get recent events")
            .Produces<EventsResponse>();

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

        // Advanced replay control endpoints
        group.MapReplayControlEndpoints();

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
            .Select(g =>
            {
                var firstEvent = g.OrderBy(e => e.Timestamp).First();
                var lastEvent = g.OrderByDescending(e => e.Timestamp).First();
                var duration = (lastEvent.Timestamp - firstEvent.Timestamp).TotalMilliseconds;

                return new FlowInfo
                {
                    CorrelationId = g.Key,
                    MessageType = firstEvent.MessageType ?? "Unknown",
                    StartTime = firstEvent.Timestamp,
                    EndTime = lastEvent.Timestamp,
                    Duration = duration,
                    EventCount = g.Count(),
                    Status = g.Any(e => e.Type == EventType.ExceptionThrown) ? "Error" : "Success",
                    HasErrors = g.Any(e => e.Type == EventType.ExceptionThrown)
                };
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

    private static async Task<Ok<EventsResponse>> GetEventsAsync(
        IEventStore eventStore,
        int? limit,
        CancellationToken ct)
    {
        var events = await eventStore.GetEventsAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            ct);

        var eventList = events
            .OrderByDescending(e => e.Timestamp)
            .Take(limit ?? 100)
            .Select(e => new DetailedEventInfo
            {
                Id = e.Id,
                Type = e.Type.ToString(),
                Timestamp = e.Timestamp,
                CorrelationId = e.CorrelationId,
                ServiceName = e.ServiceName ?? "Unknown",
                MessageType = e.MessageType ?? "Unknown",
                Duration = e.Duration,
                Status = e.Exception == null ? "Success" : "Error",
                Error = e.Exception
            })
            .ToList();

        return TypedResults.Ok(new EventsResponse
        {
            Events = eventList,
            Timestamp = DateTime.UtcNow
        });
    }

    private static async Task<Ok<StatsResponse>> GetStatsAsync(
        IEventStore eventStore,
        CancellationToken ct)
    {
        var stats = await eventStore.GetStatsAsync(ct);

        // Calculate success rate and average latency based on FLOWS, not individual events
        var recentEvents = await eventStore.GetEventsAsync(
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            ct);

        var eventsList = recentEvents.ToList();

        // Group by correlation ID to get flows
        var flows = eventsList
            .GroupBy(e => e.CorrelationId)
            .ToList();

        // Calculate success rate: flows without exceptions
        var successfulFlows = flows.Count(g => !g.Any(e => e.Type == EventType.ExceptionThrown));
        var totalFlows = flows.Count > 0 ? flows.Count : 1;
        var successRate = (double)successfulFlows / totalFlows * 100;

        // Calculate average latency: only from PerformanceMetric events
        var performanceEvents = eventsList.Where(e => e.Type == EventType.PerformanceMetric).ToList();
        var averageLatency = performanceEvents.Any()
            ? performanceEvents.Average(e => e.Duration)
            : 0;

        return TypedResults.Ok(new StatsResponse
        {
            TotalEvents = stats.TotalEvents,
            TotalFlows = stats.TotalFlows,
            StorageSizeBytes = stats.StorageSizeBytes,
            OldestEvent = stats.OldestEvent,
            NewestEvent = stats.NewestEvent,
            SuccessRate = Math.Round(successRate, 2),
            AverageLatency = Math.Round(averageLatency, 2),
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
    public required string MessageType { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required double Duration { get; init; }
    public required int EventCount { get; init; }
    public required string Status { get; init; }
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

public sealed record DetailedEventInfo
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string CorrelationId { get; init; }
    public required string ServiceName { get; init; }
    public required string MessageType { get; init; }
    public required double Duration { get; init; }
    public required string Status { get; init; }
    public string? Error { get; init; }
}

public sealed record EventsResponse
{
    public required List<DetailedEventInfo> Events { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record StatsResponse
{
    public required long TotalEvents { get; init; }
    public required long TotalFlows { get; init; }
    public required long StorageSizeBytes { get; init; }
    public required DateTime OldestEvent { get; init; }
    public required DateTime NewestEvent { get; init; }
    public required double SuccessRate { get; init; }
    public required double AverageLatency { get; init; }
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

