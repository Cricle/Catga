using Catga.Flow.HotReload;
using Microsoft.AspNetCore.Mvc;
using OrderSystem.Api;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Endpoints demonstrating Catga Flow Hot Reload features:
/// - Dynamic flow registration
/// - Version management
/// - Flow reload with events
/// </summary>
public static class HotReloadEndpoints
{
    public static void MapHotReloadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/hotreload")
            .WithTags("Hot Reload");

        // List all registered flows
        group.MapGet("/flows", (IFlowRegistry registry) =>
        {
            var flows = registry.GetAll().ToList();
            return Results.Ok(new FlowListResponse(flows.Count, flows));
        }).WithName("ListRegisteredFlows");

        // Get flow details
        group.MapGet("/flows/{flowName}", (string flowName, IFlowRegistry registry, IFlowVersionManager versionManager) =>
        {
            var config = registry.Get(flowName);
            if (config == null)
            {
                return Results.NotFound(new ErrorResponse($"Flow '{flowName}' not found"));
            }

            return Results.Ok(new FlowDetailsResponse(flowName, versionManager.GetCurrentVersion(flowName), config.GetType().Name, true));
        }).WithName("GetFlowDetails");

        // Register a demo flow
        group.MapPost("/flows/{flowName}", (string flowName, IFlowRegistry registry, IFlowVersionManager versionManager) =>
        {
            var config = new { Name = flowName, RegisteredAt = DateTime.UtcNow };
            registry.Register(flowName, config);
            versionManager.SetVersion(flowName, 1);

            return Results.Created($"/api/hotreload/flows/{flowName}", new FlowRegisteredResponse2($"Flow '{flowName}' registered", 1));
        }).WithName("RegisterFlow");

        // Reload a flow (increment version)
        group.MapPut("/flows/{flowName}/reload", async (
            string flowName,
            IFlowReloader reloader,
            IFlowVersionManager versionManager) =>
        {
            var oldVersion = versionManager.GetCurrentVersion(flowName);
            var newConfig = new { Name = flowName, ReloadedAt = DateTime.UtcNow };

            await reloader.ReloadAsync(flowName, newConfig);

            var newVersion = versionManager.GetCurrentVersion(flowName);

            return Results.Ok(new FlowReloadedResponse2($"Flow '{flowName}' reloaded", oldVersion, newVersion));
        }).WithName("ReloadFlow");

        // Unregister a flow
        group.MapDelete("/flows/{flowName}", (string flowName, IFlowRegistry registry) =>
        {
            var removed = registry.Unregister(flowName);
            if (!removed)
            {
                return Results.NotFound(new ErrorResponse($"Flow '{flowName}' not found"));
            }

            return Results.Ok(new MessageResponse($"Flow '{flowName}' unregistered"));
        }).WithName("UnregisterFlow");

        // Get version info
        group.MapGet("/versions/{flowName}", (string flowName, IFlowVersionManager versionManager) =>
        {
            var version = versionManager.GetCurrentVersion(flowName);
            return Results.Ok(new FlowVersionResponse(flowName, version));
        }).WithName("GetFlowVersion");

        // Demo: Subscribe to reload events
        group.MapGet("/events/info", () =>
        {
            return new ReloadEventInfoResponse(
                nameof(FlowReloadedEvent),
                new[]
                {
                    "FlowName - Name of the reloaded flow",
                    "OldVersion - Version before reload",
                    "NewVersion - Version after reload",
                    "ReloadedAt - UTC timestamp of reload"
                },
                @"
reloader.FlowReloaded += (sender, e) =>
{
    Console.WriteLine($""Flow {e.FlowName} reloaded: v{e.OldVersion} -> v{e.NewVersion}"");
};
");
        }).WithName("GetReloadEventInfo");
    }
}
