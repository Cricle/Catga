using Catga.Abstractions;
using Catga.EventSourcing;
using Microsoft.AspNetCore.Mvc;

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
            return Results.Ok(new
            {
                LastSyncTime = lastSync,
                Status = lastSync.HasValue ? "Active" : "Never synced"
            });
        }).WithName("GetSyncStatus");

        // Get pending changes
        group.MapGet("/pending", async (IChangeTracker tracker) =>
        {
            var pending = await tracker.GetPendingChangesAsync();
            return Results.Ok(new
            {
                Count = pending.Count,
                Changes = pending.Select(c => new
                {
                    c.Id,
                    c.EntityType,
                    c.EntityId,
                    c.Type,
                    c.Timestamp,
                    c.IsSynced
                })
            });
        }).WithName("GetPendingChanges");

        // Trigger sync
        group.MapPost("/sync", async (IReadModelSynchronizer synchronizer) =>
        {
            var before = DateTime.UtcNow;
            await synchronizer.SyncAsync();
            var after = await synchronizer.GetLastSyncTimeAsync();

            return Results.Ok(new
            {
                Message = "Sync completed",
                SyncedAt = after,
                DurationMs = after.HasValue ? (after.Value - before).TotalMilliseconds : 0
            });
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

            return Results.Created($"/api/readmodelsync/pending", new
            {
                Message = "Change tracked",
                ChangeId = change.Id,
                EntityType = entityType,
                EntityId = entityId,
                ChangeType = changeType.ToString()
            });
        }).WithName("TrackChange");

        // Mark changes as synced
        group.MapPost("/mark-synced", async (
            [FromBody] string[] changeIds,
            IChangeTracker tracker) =>
        {
            await tracker.MarkAsSyncedAsync(changeIds);

            return Results.Ok(new
            {
                Message = "Changes marked as synced",
                MarkedCount = changeIds.Length
            });
        }).WithName("MarkChangesSynced");

        // Get strategy info
        group.MapGet("/strategies", () =>
        {
            return new
            {
                Available = new[]
                {
                    new
                    {
                        Name = "Realtime",
                        Description = "Sync immediately on each change",
                        UseCase = "Low latency requirements"
                    },
                    new
                    {
                        Name = "Batch",
                        Description = "Collect changes and process in batches",
                        UseCase = "High throughput scenarios"
                    },
                    new
                    {
                        Name = "Scheduled",
                        Description = "Sync at scheduled intervals",
                        UseCase = "Eventual consistency with lower resource usage"
                    }
                },
                Registration = new
                {
                    Realtime = "services.AddReadModelSync(change => ProcessAsync(change))",
                    Batch = "services.AddReadModelSyncWithBatching(100, batch => BulkProcessAsync(batch))",
                    Scheduled = "services.AddReadModelSyncWithSchedule(TimeSpan.FromMinutes(5), changes => SyncAsync(changes))"
                }
            };
        }).WithName("GetSyncStrategies");
    }

    private record DemoEvent(string EntityId, string Description) : IEvent
    {
        public long MessageId => 0;
    }
}
