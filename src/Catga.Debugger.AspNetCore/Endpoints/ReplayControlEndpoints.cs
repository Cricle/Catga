using Catga.Debugger.Models;
using Catga.Debugger.Replay;
using Catga.Debugger.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Catga.Debugger.AspNetCore.Endpoints;

/// <summary>
/// Advanced replay control endpoints for time-travel debugging
/// Provides step-by-step execution, state inspection, and timeline navigation
/// </summary>
public static class ReplayControlEndpoints
{
    public static RouteGroupBuilder MapReplayControlEndpoints(this RouteGroupBuilder group)
    {
        // Flow replay step control
        group.MapPost("/replay/flow/{correlationId}/step", StepFlowAsync)
            .WithName("StepFlow")
            .WithSummary("Step forward in flow replay")
            .Produces<FlowStepResponse>();

        group.MapPost("/replay/flow/{correlationId}/jump", JumpFlowAsync)
            .WithName("JumpFlow")
            .WithSummary("Jump to timestamp in flow replay")
            .Produces<FlowStepResponse>();

        group.MapGet("/replay/flow/{correlationId}/state", GetFlowStateAsync)
            .WithName("GetFlowState")
            .WithSummary("Get current flow state with variables")
            .Produces<FlowStateResponse>();

        group.MapGet("/replay/flow/{correlationId}/timeline", GetFlowTimelineAsync)
            .WithName("GetFlowTimeline")
            .WithSummary("Get flow event timeline for visualization")
            .Produces<FlowTimelineResponse>();

        group.MapDelete("/replay/flow/{correlationId}", EndFlowReplayAsync)
            .WithName("EndFlowReplay")
            .WithSummary("End flow replay session")
            .Produces(204);

        return group;
    }

    private static async Task<Results<Ok<FlowStepResponse>, NotFound>> StepFlowAsync(
        string correlationId,
        [FromBody] StepRequest? request,
        IReplayEngine replayEngine,
        ReplaySessionManager sessionManager,
        CancellationToken ct)
    {
        var replay = sessionManager.GetFlowReplay(correlationId);
        if (replay == null)
        {
            // Create new session
            replay = await replayEngine.ReplayFlowAsync(correlationId, ct);
            sessionManager.GetOrCreateFlowReplay(correlationId, () => replay);
        }

        int steps = request?.Steps ?? 1;
        await replay.StepAsync(steps);

        var machine = replay.StateMachine;
        var currentEvent = machine.CurrentIndex < machine.TotalSteps 
            ? GetEventAtIndex(machine, machine.CurrentIndex) 
            : null;

        return TypedResults.Ok(new FlowStepResponse
        {
            CorrelationId = correlationId,
            CurrentStep = machine.CurrentIndex,
            TotalSteps = machine.TotalSteps,
            CurrentEvent = currentEvent != null ? new ReplayEventInfo
            {
                Type = currentEvent.Type.ToString(),
                Timestamp = currentEvent.Timestamp,
                Data = currentEvent.Data?.ToString()
            } : null,
            Variables = machine.Variables,
            HasNext = machine.CurrentIndex < machine.TotalSteps - 1,
            HasPrevious = machine.CurrentIndex > 0
        });
    }

    private static async Task<Results<Ok<FlowStepResponse>, NotFound>> JumpFlowAsync(
        string correlationId,
        [FromBody] JumpRequest request,
        ReplaySessionManager sessionManager,
        CancellationToken ct)
    {
        var replay = sessionManager.GetFlowReplay(correlationId);
        if (replay == null)
            return TypedResults.NotFound();

        await replay.JumpToTimestampAsync(request.Timestamp);

        var machine = replay.StateMachine;
        var currentEvent = machine.CurrentIndex < machine.TotalSteps
            ? GetEventAtIndex(machine, machine.CurrentIndex)
            : null;

        return TypedResults.Ok(new FlowStepResponse
        {
            CorrelationId = correlationId,
            CurrentStep = machine.CurrentIndex,
            TotalSteps = machine.TotalSteps,
            CurrentEvent = currentEvent != null ? new ReplayEventInfo
            {
                Type = currentEvent.Type.ToString(),
                Timestamp = currentEvent.Timestamp,
                Data = currentEvent.Data?.ToString()
            } : null,
            Variables = machine.Variables,
            HasNext = machine.CurrentIndex < machine.TotalSteps - 1,
            HasPrevious = machine.CurrentIndex > 0
        });
    }

    private static async Task<Results<Ok<FlowStateResponse>, NotFound>> GetFlowStateAsync(
        string correlationId,
        ReplaySessionManager sessionManager,
        CancellationToken ct)
    {
        var replay = sessionManager.GetFlowReplay(correlationId);
        if (replay == null)
            return TypedResults.NotFound();

        await Task.CompletedTask;

        var machine = replay.StateMachine;
        return TypedResults.Ok(new FlowStateResponse
        {
            CorrelationId = correlationId,
            CurrentStep = machine.CurrentIndex,
            TotalSteps = machine.TotalSteps,
            Variables = machine.Variables,
            CallStack = machine.CallStack.Select(f => new CallFrameInfo
            {
                MethodName = f.MethodName,
                FileName = f.FileName,
                LineNumber = f.LineNumber
            }).ToList(),
            CurrentState = machine.CurrentState?.ToString()
        });
    }

    private static async Task<Results<Ok<FlowTimelineResponse>, NotFound>> GetFlowTimelineAsync(
        string correlationId,
        IEventStore eventStore,
        CancellationToken ct)
    {
        var events = await eventStore.GetEventsByCorrelationAsync(correlationId, ct);
        var eventsList = events.OrderBy(e => e.Timestamp).ToList();

        if (!eventsList.Any())
            return TypedResults.NotFound();

        var timeline = eventsList.Select((e, index) => new TimelinePoint
        {
            Index = index,
            Timestamp = e.Timestamp,
            Type = e.Type.ToString(),
            Duration = e.Duration,
            HasError = !string.IsNullOrEmpty(e.Exception)
        }).ToList();

        return TypedResults.Ok(new FlowTimelineResponse
        {
            CorrelationId = correlationId,
            Timeline = timeline,
            StartTime = eventsList.First().Timestamp,
            EndTime = eventsList.Last().Timestamp,
            TotalDuration = (eventsList.Last().Timestamp - eventsList.First().Timestamp).TotalMilliseconds
        });
    }

    private static Task<NoContent> EndFlowReplayAsync(
        string correlationId,
        ReplaySessionManager sessionManager,
        CancellationToken ct)
    {
        sessionManager.RemoveFlowReplay(correlationId);
        return Task.FromResult(TypedResults.NoContent());
    }

    // Helper method to safely get event from state machine
    private static ReplayableEvent? GetEventAtIndex(FlowStateMachine machine, int index)
    {
        // Access private field via reflection (for demo purposes)
        // In production, FlowStateMachine should expose GetEventAtIndex method
        try
        {
            var field = typeof(FlowStateMachine).GetField("_events", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(machine) is List<ReplayableEvent> events && index < events.Count)
            {
                return events[index];
            }
        }
        catch { }
        return null;
    }
}

// Request types
public sealed record StepRequest
{
    public int Steps { get; init; } = 1;
}

public sealed record JumpRequest
{
    public required DateTime Timestamp { get; init; }
}

// Response types
public sealed record FlowStepResponse
{
    public required string CorrelationId { get; init; }
    public required int CurrentStep { get; init; }
    public required int TotalSteps { get; init; }
    public ReplayEventInfo? CurrentEvent { get; init; }
    public required Dictionary<string, object?> Variables { get; init; }
    public required bool HasNext { get; init; }
    public required bool HasPrevious { get; init; }
}

public sealed record ReplayEventInfo
{
    public required string Type { get; init; }
    public required DateTime Timestamp { get; init; }
    public string? Data { get; init; }
}

public sealed record FlowStateResponse
{
    public required string CorrelationId { get; init; }
    public required int CurrentStep { get; init; }
    public required int TotalSteps { get; init; }
    public required Dictionary<string, object?> Variables { get; init; }
    public required List<CallFrameInfo> CallStack { get; init; }
    public string? CurrentState { get; init; }
}

public sealed record CallFrameInfo
{
    public required string MethodName { get; init; }
    public string? FileName { get; init; }
    public int LineNumber { get; init; }
}

public sealed record FlowTimelineResponse
{
    public required string CorrelationId { get; init; }
    public required List<TimelinePoint> Timeline { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required double TotalDuration { get; init; }
}

public sealed record TimelinePoint
{
    public required int Index { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Type { get; init; }
    public required double Duration { get; init; }
    public required bool HasError { get; init; }
}

