using Catga.Abstractions;
using Catga.EventSourcing;
using Microsoft.AspNetCore.Mvc;
using OrderSystem.Api;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Endpoints demonstrating Catga Read Model Sync features:
/// - Change tracking
/// - Sync strategies (Realtime, Batch, Scheduled)
/// - Synchronization status
/// </summary>
public static class ReadModelSyncEndpoints
{
    public static void MapReadModelSyncEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/readmodelsync")
            .WithTags("Read Model Sync");

        // Get sync status
        group.MapGet("/status", async (IReadModelSynchronizer synchronizer) =>
        {
            var lastSync = await synchronizer.GetLastSyncTimeAsync();
            return Results.Ok(new SyncStatusResponse(lastSync.HasValue ? "Active" : "Never synced", 0, lastSync ?? DateTime.UtcNow));
        }).WithName("GetSyncStatus");

        // Get pending changes
        group.MapGet("/pending", async (IChangeTracker tracker) =>
        {
            var pending = await tracker.GetPendingChangesAsync();
            return Results.Ok(pending);
        }).WithName("GetPendingChanges");

        // Trigger sync
        group.MapPost("/sync", async (IReadModelSynchronizer synchronizer) =>
        {
            var before = DateTime.UtcNow;
            await synchronizer.SyncAsync();
            var after = await synchronizer.GetLastSyncTimeAsync();

            return Results.Ok(new RebuildReadModelResponse("Sync completed", 0));
        }).WithName("TriggerSync");

        // Demo: Track a change
        group.MapPost("/demo/track", (
            [FromQuery] string entityType,
            [FromQuery] string entityId,
            [FromQuery] ChangeType changeType,
            IChangeTracker tracker) =>
        {
            var change = new ChangeRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityType = entityType,
                EntityId = entityId,
                Type = changeType,
                Event = new DemoEvent(entityId, $"{changeType} on {entityType}")
            };

            tracker.TrackChange(change);

            return Results.Created($"/api/readmodelsync/pending", new MessageResponse("Change tracked"));
        }).WithName("TrackChange");

        // Mark changes as synced
        group.MapPost("/mark-synced", async (
            [FromBody] string[] changeIds,
            IChangeTracker tracker) =>
        {
            await tracker.MarkAsSyncedAsync(changeIds);

            return Results.Ok(new RebuildReadModelResponse("Changes marked as synced", changeIds.Length));
        }).WithName("MarkChangesSynced");

        // Get strategy info
        group.MapGet("/strategies", () =>
        {
            return Results.Ok(new MetricsResponse(new Dictionary<string, object>
            {
                { "Available", new[] { "Realtime", "Batch", "Scheduled" } },
                { "Registration", "See documentation" }
            }));
        }).WithName("GetSyncStrategies");
    }

    private record DemoEvent(string EntityId, string Description) : IEvent
    {
        public long MessageId => 0;
    }
}
