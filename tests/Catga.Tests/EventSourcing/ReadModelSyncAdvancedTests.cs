using Catga.Abstractions;
using Catga.EventSourcing;
using FluentAssertions;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Advanced tests for Read Model Sync components
/// </summary>
public class ReadModelSyncAdvancedTests
{
    private record TestEvent(string Message) : IEvent
    {
        public long MessageId => 0;
    }

    #region ChangeRecord Advanced Tests

    [Fact]
    public void ChangeRecord_Timestamp_ShouldDefaultToUtcNow()
    {
        var before = DateTime.UtcNow;
        var change = new ChangeRecord
        {
            Id = "1",
            EntityType = "Order",
            EntityId = "order-1",
            Type = ChangeType.Created,
            Event = new TestEvent("test")
        };
        var after = DateTime.UtcNow;

        change.Timestamp.Should().BeOnOrAfter(before);
        change.Timestamp.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void ChangeRecord_IsSynced_ShouldDefaultToFalse()
    {
        var change = CreateChange("1");
        change.IsSynced.Should().BeFalse();
    }

    [Fact]
    public void ChangeRecord_IsSynced_CanBeModified()
    {
        var change = CreateChange("1");
        change.IsSynced = true;
        change.IsSynced.Should().BeTrue();
    }

    [Fact]
    public void ChangeType_ShouldHaveAllValues()
    {
        Enum.GetValues<ChangeType>().Should().HaveCount(3);
        Enum.IsDefined(ChangeType.Created).Should().BeTrue();
        Enum.IsDefined(ChangeType.Updated).Should().BeTrue();
        Enum.IsDefined(ChangeType.Deleted).Should().BeTrue();
    }

    #endregion

    #region InMemoryChangeTracker Advanced Tests

    [Fact]
    public async Task InMemoryChangeTracker_MultipleChanges_ShouldTrackAll()
    {
        var tracker = new InMemoryChangeTracker();

        for (int i = 0; i < 100; i++)
        {
            tracker.TrackChange(CreateChange($"change-{i}"));
        }

        var pending = await tracker.GetPendingChangesAsync();
        pending.Should().HaveCount(100);
    }

    [Fact]
    public async Task InMemoryChangeTracker_MarkAsSynced_PartialList_ShouldOnlyMarkSpecified()
    {
        var tracker = new InMemoryChangeTracker();
        tracker.TrackChange(CreateChange("1"));
        tracker.TrackChange(CreateChange("2"));
        tracker.TrackChange(CreateChange("3"));

        await tracker.MarkAsSyncedAsync(new[] { "1", "3" });

        var pending = await tracker.GetPendingChangesAsync();
        pending.Should().HaveCount(1);
        pending[0].Id.Should().Be("2");
    }

    [Fact]
    public async Task InMemoryChangeTracker_MarkAsSynced_NonExistentId_ShouldNotThrow()
    {
        var tracker = new InMemoryChangeTracker();
        tracker.TrackChange(CreateChange("1"));

        var act = async () => await tracker.MarkAsSyncedAsync(new[] { "nonexistent" });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InMemoryChangeTracker_MarkAsSynced_EmptyList_ShouldNotThrow()
    {
        var tracker = new InMemoryChangeTracker();
        tracker.TrackChange(CreateChange("1"));

        var act = async () => await tracker.MarkAsSyncedAsync(Array.Empty<string>());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InMemoryChangeTracker_GetPendingChangesAsync_EmptyTracker_ShouldReturnEmpty()
    {
        var tracker = new InMemoryChangeTracker();

        var pending = await tracker.GetPendingChangesAsync();

        pending.Should().BeEmpty();
    }

    #endregion

    #region BatchSyncStrategy Advanced Tests

    [Fact]
    public async Task BatchSyncStrategy_EmptyChanges_ShouldNotCallAction()
    {
        var called = false;
        var strategy = new BatchSyncStrategy(10, _ =>
        {
            called = true;
            return ValueTask.CompletedTask;
        });

        await strategy.ExecuteAsync(Array.Empty<ChangeRecord>());

        called.Should().BeFalse();
    }

    [Fact]
    public async Task BatchSyncStrategy_ExactBatchSize_ShouldCallOnce()
    {
        var callCount = 0;
        var strategy = new BatchSyncStrategy(5, _ =>
        {
            callCount++;
            return ValueTask.CompletedTask;
        });

        var changes = Enumerable.Range(0, 5).Select(i => CreateChange($"{i}")).ToList();
        await strategy.ExecuteAsync(changes);

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task BatchSyncStrategy_LargeBatchSize_ShouldStillProcess()
    {
        var processedCount = 0;
        var strategy = new BatchSyncStrategy(1000, batch =>
        {
            processedCount += batch.Count;
            return ValueTask.CompletedTask;
        });

        var changes = Enumerable.Range(0, 50).Select(i => CreateChange($"{i}")).ToList();
        await strategy.ExecuteAsync(changes);

        processedCount.Should().Be(50);
    }

    #endregion

    #region ScheduledSyncStrategy Advanced Tests

    [Fact]
    public async Task ScheduledSyncStrategy_FirstCall_ShouldSync()
    {
        var synced = false;
        var strategy = new ScheduledSyncStrategy(TimeSpan.FromMinutes(1), _ =>
        {
            synced = true;
            return ValueTask.CompletedTask;
        });

        var changes = new[] { CreateChange("1") };
        await strategy.ExecuteAsync(changes);

        synced.Should().BeTrue();
    }

    [Fact]
    public async Task ScheduledSyncStrategy_EmptyChanges_ShouldNotSync()
    {
        var synced = false;
        var strategy = new ScheduledSyncStrategy(TimeSpan.Zero, _ =>
        {
            synced = true;
            return ValueTask.CompletedTask;
        });

        await strategy.ExecuteAsync(Array.Empty<ChangeRecord>());

        synced.Should().BeFalse();
    }

    #endregion

    #region RealtimeSyncStrategy Advanced Tests

    [Fact]
    public async Task RealtimeSyncStrategy_EmptyChanges_ShouldNotCallAction()
    {
        var called = false;
        var strategy = new RealtimeSyncStrategy(_ =>
        {
            called = true;
            return ValueTask.CompletedTask;
        });

        await strategy.ExecuteAsync(Array.Empty<ChangeRecord>());

        called.Should().BeFalse();
    }

    [Fact]
    public async Task RealtimeSyncStrategy_PreservesOrder()
    {
        var processedIds = new List<string>();
        var strategy = new RealtimeSyncStrategy(c =>
        {
            processedIds.Add(c.Id);
            return ValueTask.CompletedTask;
        });

        var changes = new[] { CreateChange("1"), CreateChange("2"), CreateChange("3") };
        await strategy.ExecuteAsync(changes);

        processedIds.Should().Equal("1", "2", "3");
    }

    #endregion

    #region DefaultReadModelSynchronizer Advanced Tests

    [Fact]
    public async Task DefaultReadModelSynchronizer_NoPendingChanges_ShouldNotUpdateLastSyncTime()
    {
        var tracker = new InMemoryChangeTracker();
        var strategy = new RealtimeSyncStrategy(_ => ValueTask.CompletedTask);
        var synchronizer = new DefaultReadModelSynchronizer(tracker, strategy);

        await synchronizer.SyncAsync();

        var lastSync = await synchronizer.GetLastSyncTimeAsync();
        lastSync.Should().BeNull();
    }

    [Fact]
    public async Task DefaultReadModelSynchronizer_MultipleSyncs_ShouldUpdateLastSyncTime()
    {
        var tracker = new InMemoryChangeTracker();
        var strategy = new RealtimeSyncStrategy(_ => ValueTask.CompletedTask);
        var synchronizer = new DefaultReadModelSynchronizer(tracker, strategy);

        tracker.TrackChange(CreateChange("1"));
        await synchronizer.SyncAsync();
        var firstSync = await synchronizer.GetLastSyncTimeAsync();

        await Task.Delay(10);

        tracker.TrackChange(CreateChange("2"));
        await synchronizer.SyncAsync();
        var secondSync = await synchronizer.GetLastSyncTimeAsync();

        secondSync.Should().BeAfter(firstSync!.Value);
    }

    #endregion

    private static ChangeRecord CreateChange(string id) => new()
    {
        Id = id,
        EntityType = "Test",
        EntityId = $"entity-{id}",
        Type = ChangeType.Created,
        Event = new TestEvent($"test-{id}")
    };
}
