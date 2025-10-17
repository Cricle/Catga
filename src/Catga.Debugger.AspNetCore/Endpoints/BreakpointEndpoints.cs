using Catga.Debugger.Breakpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Catga.Debugger.AspNetCore.Endpoints;

/// <summary>
/// API endpoints for breakpoint management
/// </summary>
public static class BreakpointEndpoints
{
    public static RouteGroupBuilder MapBreakpointEndpoints(this RouteGroupBuilder group)
    {
        // Get all breakpoints
        group.MapGet("/breakpoints", GetAllBreakpointsAsync)
            .WithName("GetAllBreakpoints")
            .WithSummary("Gets all breakpoints")
            .Produces<List<BreakpointDto>>();

        // Add a breakpoint
        group.MapPost("/breakpoints", AddBreakpointAsync)
            .WithName("AddBreakpoint")
            .WithSummary("Adds a new breakpoint")
            .Produces<BreakpointDto>(201)
            .Produces(400);

        // Remove a breakpoint
        group.MapDelete("/breakpoints/{id}", RemoveBreakpointAsync)
            .WithName("RemoveBreakpoint")
            .WithSummary("Removes a breakpoint")
            .Produces(204)
            .Produces(404);

        // Toggle breakpoint enabled/disabled
        group.MapPost("/breakpoints/{id}/toggle", ToggleBreakpointAsync)
            .WithName("ToggleBreakpoint")
            .WithSummary("Enables or disables a breakpoint")
            .Produces<BreakpointDto>()
            .Produces(404);

        // Continue execution from a breakpoint
        group.MapPost("/breakpoints/continue/{correlationId}", ContinueExecutionAsync)
            .WithName("ContinueExecution")
            .WithSummary("Continues execution from a breakpoint")
            .Produces(204)
            .Produces(404);

        return group;
    }

    private static Results<Ok<List<BreakpointDto>>, NotFound> GetAllBreakpointsAsync(
        BreakpointManager breakpointManager)
    {
        var breakpoints = breakpointManager.GetAllBreakpoints();
        var dtos = breakpoints.Select(ToDto).ToList();
        return TypedResults.Ok(dtos);
    }

    private static Results<Created<BreakpointDto>, BadRequest> AddBreakpointAsync(
        [FromBody] AddBreakpointRequest request,
        BreakpointManager breakpointManager)
    {
        BreakpointCondition condition;

        switch (request.ConditionType?.ToLower())
        {
            case "messagetype":
                condition = BreakpointCondition.MessageType(
                    request.Id,
                    request.MessageType ?? "");
                break;

            case "always":
            default:
                condition = BreakpointCondition.Always(request.Id, request.Name);
                break;
        }

        var breakpoint = new Breakpoint(
            request.Id,
            request.Name,
            condition,
            enabled: request.Enabled ?? true
        );

        if (!breakpointManager.AddBreakpoint(breakpoint))
        {
            return TypedResults.BadRequest();
        }

        return TypedResults.Created($"/debug-api/breakpoints/{breakpoint.Id}", ToDto(breakpoint));
    }

    private static Results<NoContent, NotFound> RemoveBreakpointAsync(
        string id,
        BreakpointManager breakpointManager)
    {
        if (breakpointManager.RemoveBreakpoint(id))
        {
            return TypedResults.NoContent();
        }
        return TypedResults.NotFound();
    }

    private static Results<Ok<BreakpointDto>, NotFound> ToggleBreakpointAsync(
        string id,
        [FromBody] ToggleBreakpointRequest request,
        BreakpointManager breakpointManager)
    {
        if (!breakpointManager.ToggleBreakpoint(id, request.Enabled))
        {
            return TypedResults.NotFound();
        }

        var breakpoint = breakpointManager.GetBreakpoint(id);
        if (breakpoint == null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ToDto(breakpoint));
    }

    private static Results<NoContent, NotFound> ContinueExecutionAsync(
        string correlationId,
        [FromBody] ContinueExecutionRequest request,
        BreakpointManager breakpointManager)
    {
        var action = request.Action?.ToLower() switch
        {
            "stepover" => DebugAction.StepOver,
            "stepinto" => DebugAction.StepInto,
            "stepout" => DebugAction.StepOut,
            _ => DebugAction.Continue
        };

        if (breakpointManager.Continue(correlationId, action))
        {
            return TypedResults.NoContent();
        }
        return TypedResults.NotFound();
    }

    private static BreakpointDto ToDto(Breakpoint breakpoint)
    {
        return new BreakpointDto
        {
            Id = breakpoint.Id,
            Name = breakpoint.Name,
            Enabled = breakpoint.Enabled,
            ConditionExpression = breakpoint.Condition.Expression,
            HitCount = breakpoint.HitCount,
            CreatedAt = breakpoint.CreatedAt,
            LastHitAt = breakpoint.LastHitAt,
            LastCorrelationId = breakpoint.LastCorrelationId
        };
    }
}

// Request/Response DTOs
public sealed record AddBreakpointRequest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ConditionType { get; init; } = "always";
    public string? MessageType { get; init; }
    public bool? Enabled { get; init; } = true;
}

public sealed record ToggleBreakpointRequest
{
    public required bool Enabled { get; init; }
}

public sealed record ContinueExecutionRequest
{
    public string Action { get; init; } = "continue";
}

public sealed record BreakpointDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool Enabled { get; init; }
    public required string ConditionExpression { get; init; }
    public required int HitCount { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? LastHitAt { get; init; }
    public string? LastCorrelationId { get; init; }
}

