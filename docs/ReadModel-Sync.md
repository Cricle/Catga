# Read Model Synchronization

CQRS Read Model Sync provides automatic synchronization between event store and read models with multiple strategies.

## Features

- **Multiple Sync Strategies**: Realtime, Batch, Scheduled
- **Change Tracking**: Track pending changes for synchronization
- **Projection Integration**: Works with existing projections
- **Extensible**: Implement custom strategies

## Quick Start

### 1. Add Services (Realtime)

```csharp
services.AddReadModelSync(async change =>
{
    // Handle each change in realtime
    await UpdateReadModelAsync(change);
});
```

### 2. Add Services (Batch)

```csharp
services.AddReadModelSyncWithBatching(100, async batch =>
{
    // Process changes in batches of 100
    await BulkUpdateAsync(batch);
});
```

### 3. Add Services (Scheduled)

```csharp
services.AddReadModelSyncWithSchedule(TimeSpan.FromMinutes(5), async changes =>
{
    // Sync every 5 minutes
    await SyncAllAsync(changes);
});
```

## Interfaces

### IReadModelSynchronizer

```csharp
public interface IReadModelSynchronizer
{
    ValueTask SyncAsync(CancellationToken ct = default);
    ValueTask<DateTime?> GetLastSyncTimeAsync(CancellationToken ct = default);
}
```

### ISyncStrategy

```csharp
public interface ISyncStrategy
{
    string Name { get; }
    ValueTask ExecuteAsync(IEnumerable<ChangeRecord> changes, CancellationToken ct = default);
}
```

### IChangeTracker

```csharp
public interface IChangeTracker
{
    void TrackChange(ChangeRecord change);
    ValueTask<IReadOnlyList<ChangeRecord>> GetPendingChangesAsync(CancellationToken ct = default);
    ValueTask MarkAsSyncedAsync(IEnumerable<string> changeIds, CancellationToken ct = default);
}
```

## Sync Strategies

### RealtimeSyncStrategy

Processes each change immediately as it occurs.

```csharp
var strategy = new RealtimeSyncStrategy(async change =>
{
    await _readModelStore.SaveAsync(change.EntityId, MapToReadModel(change));
});
```

### BatchSyncStrategy

Collects changes and processes them in batches.

```csharp
var strategy = new BatchSyncStrategy(50, async batch =>
{
    await _bulkWriter.WriteAsync(batch.Select(MapToReadModel));
});
```

### ScheduledSyncStrategy

Processes all pending changes at scheduled intervals.

```csharp
var strategy = new ScheduledSyncStrategy(TimeSpan.FromSeconds(30), async changes =>
{
    foreach (var change in changes)
    {
        await ProcessChangeAsync(change);
    }
});
```

## Projection Integration

```csharp
services.AddProjectionSync<OrderProjection>();
```

This automatically connects your projection to the sync pipeline.

## Change Types

```csharp
public enum ChangeType
{
    Created,
    Updated,
    Deleted
}
```

## Best Practices

1. **Use Realtime** for low-latency requirements
2. **Use Batch** for high-throughput scenarios
3. **Use Scheduled** for eventual consistency with lower resource usage
4. **Implement idempotency** in your sync handlers
