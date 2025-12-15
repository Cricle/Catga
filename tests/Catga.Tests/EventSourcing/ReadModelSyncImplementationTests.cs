using Catga.Abstractions;
using Catga.EventSourcing;
using FluentAssertions;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Implementation tests for Read Model Sync components
/// </summary>
public class ReadModelSyncImplementationTests
{
    private record TestEvent(string Message) : IEvent
    {
        public long MessageId => 0;
    }

    #region ChangeRecord Tests

    [Fact]
    public void ChangeRecord_CanBeCreated()
    {
        var change = new ChangeRecord
        {
            Id = "change-1",
            EntityType = "Order",
            EntityId = "order-123",
            Type = ChangeType.Created,
            Event = new TestEvent("test")
        };

        change.Id.Should().Be("change-1");
        change.EntityType.Should().Be("Order");
        change.EntityId.Should().Be("order-123");
        change.Type.Should().Be(ChangeType.Created);
        change.IsSynced.Should().BeFalse();
    }

    #endregion

    #region InMemoryChangeTracker Tests

    [Fact]
    public void InMemoryChangeTracker_TrackChange_AddsChange()
    {
        var tracker = new InMemoryChangeTracker();
        var change = CreateTestChange("1");

        tracker.TrackChange(change);

        var pending = tracker.GetPendingChangesAsync().AsTask().Result;
        pending.Should().HaveCount(1);
    }

    [Fact]
    public async Task InMemoryChangeTracker_GetPendingChangesAsync_ReturnsOnlyUnsynced()
    {
        var tracker = new InMemoryChangeTracker();
        tracker.TrackChange(CreateTestChange("1"));
        tracker.TrackChange(CreateTestChange("2"));

        await tracker.MarkAsSyncedAsync(new[] { "1" });

        var pending = await tracker.GetPendingChangesAsync();
        pending.Should().HaveCount(1);
        pending[0].Id.Should().Be("2");
    }

    [Fact]
    public async Task InMemoryChangeTracker_MarkAsSyncedAsync_MarksChanges()
    {
        var tracker = new InMemoryChangeTracker();
        var change = CreateTestChange("1");
        tracker.TrackChange(change);

        await tracker.MarkAsSyncedAsync(new[] { "1" });

        var pending = await tracker.GetPendingChangesAsync();
        pending.Should().BeEmpty();
    }

    #endregion

    #region RealtimeSyncStrategy Tests

    [Fact]
    public async Task RealtimeSyncStrategy_ExecutesForEachChange()
    {
        var syncedChanges = new List<ChangeRecord>();
        var strategy = new RealtimeSyncStrategy(c =>
        {
            syncedChanges.Add(c);
            return ValueTask.CompletedTask;
        });

        var changes = new[] { CreateTestChange("1"), CreateTestChange("2") };
        await strategy.ExecuteAsync(changes);

        syncedChanges.Should().HaveCount(2);
    }

    [Fact]
    public void RealtimeSyncStrategy_HasCorrectName()
    {
        var strategy = new RealtimeSyncStrategy(_ => ValueTask.CompletedTask);
        strategy.Name.Should().Be("Realtime");
    }

    #endregion

    #region BatchSyncStrategy Tests

    [Fact]
    public async Task BatchSyncStrategy_BatchesChanges()
    {
        var batches = new List<int>();
        var strategy = new BatchSyncStrategy(2, batch =>
        {
            batches.Add(batch.Count);
            return ValueTask.CompletedTask;
        });

        var changes = new[] { CreateTestChange("1"), CreateTestChange("2"), CreateTestChange("3") };
        await strategy.ExecuteAsync(changes);

        batches.Should().HaveCount(2);
        batches[0].Should().Be(2);
        batches[1].Should().Be(1);
    }

    [Fact]
    public void BatchSyncStrategy_HasCorrectName()
    {
        var strategy = new BatchSyncStrategy(10, _ => ValueTask.CompletedTask);
        strategy.Name.Should().Be("Batch");
    }

    #endregion

    #region ScheduledSyncStrategy Tests

    [Fact]
    public void ScheduledSyncStrategy_HasCorrectName()
    {
        var strategy = new ScheduledSyncStrategy(TimeSpan.FromMinutes(1), _ => ValueTask.CompletedTask);
        strategy.Name.Should().Be("Scheduled");
    }

    #endregion

    #region DefaultReadModelSynchronizer Tests

    [Fact]
    public async Task DefaultReadModelSynchronizer_SyncAsync_SyncsAndMarksChanges()
    {
        var tracker = new InMemoryChangeTracker();
        tracker.TrackChange(CreateTestChange("1"));
        tracker.TrackChange(CreateTestChange("2"));

        var syncedCount = 0;
        var strategy = new RealtimeSyncStrategy(_ =>
        {
            syncedCount++;
            return ValueTask.CompletedTask;
        });

        var synchronizer = new DefaultReadModelSynchronizer(tracker, strategy);
        await synchronizer.SyncAsync();

        syncedCount.Should().Be(2);
        var pending = await tracker.GetPendingChangesAsync();
        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultReadModelSynchronizer_GetLastSyncTimeAsync_ReturnsNullInitially()
    {
        var tracker = new InMemoryChangeTracker();
        var strategy = new RealtimeSyncStrategy(_ => ValueTask.CompletedTask);
        var synchronizer = new DefaultReadModelSynchronizer(tracker, strategy);

        var lastSync = await synchronizer.GetLastSyncTimeAsync();

        lastSync.Should().BeNull();
    }

    [Fact]
    public async Task DefaultReadModelSynchronizer_GetLastSyncTimeAsync_ReturnsTimeAfterSync()
    {
        var tracker = new InMemoryChangeTracker();
        tracker.TrackChange(CreateTestChange("1"));
        var strategy = new RealtimeSyncStrategy(_ => ValueTask.CompletedTask);
        var synchronizer = new DefaultReadModelSynchronizer(tracker, strategy);

        await synchronizer.SyncAsync();
        var lastSync = await synchronizer.GetLastSyncTimeAsync();

        lastSync.Should().NotBeNull();
        lastSync.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    #endregion

    private static ChangeRecord CreateTestChange(string id) => new()
    {
        Id = id,
        EntityType = "Test",
        EntityId = $"entity-{id}",
        Type = ChangeType.Created,
        Event = new TestEvent($"test-{id}")
    };
}
