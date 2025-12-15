using FluentAssertions;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// TDD tests for CQRS Read Model Synchronization
/// </summary>
public class ReadModelSyncTests
{
    #region Interface Tests (Open-Closed Principle)

    [Fact]
    public void IReadModelSynchronizer_ShouldExist()
    {
        var type = Type.GetType("Catga.EventSourcing.IReadModelSynchronizer, Catga");

        type.Should().NotBeNull("IReadModelSynchronizer interface should exist");
    }

    [Fact]
    public void IReadModelSynchronizer_ShouldDefineSyncMethods()
    {
        var type = Type.GetType("Catga.EventSourcing.IReadModelSynchronizer, Catga");

        type.Should().NotBeNull();
        type!.GetMethod("SyncAsync").Should().NotBeNull();
        type.GetMethod("GetLastSyncTimeAsync").Should().NotBeNull();
    }

    [Fact]
    public void ISyncStrategy_ShouldExist()
    {
        var type = Type.GetType("Catga.EventSourcing.ISyncStrategy, Catga");

        type.Should().NotBeNull("ISyncStrategy interface should exist");
    }

    #endregion

    #region Sync Strategy Tests

    [Fact]
    public void SyncStrategy_RealtimeShouldExist()
    {
        var type = Type.GetType("Catga.EventSourcing.RealtimeSyncStrategy, Catga");

        type.Should().NotBeNull("RealtimeSyncStrategy should exist");
    }

    [Fact]
    public void SyncStrategy_BatchShouldExist()
    {
        var type = Type.GetType("Catga.EventSourcing.BatchSyncStrategy, Catga");

        type.Should().NotBeNull("BatchSyncStrategy should exist");
    }

    [Fact]
    public void SyncStrategy_ScheduledShouldExist()
    {
        var type = Type.GetType("Catga.EventSourcing.ScheduledSyncStrategy, Catga");

        type.Should().NotBeNull("ScheduledSyncStrategy should exist");
    }

    #endregion

    #region Change Tracker Tests

    [Fact]
    public void IChangeTracker_ShouldExist()
    {
        var type = Type.GetType("Catga.EventSourcing.IChangeTracker, Catga");

        type.Should().NotBeNull("IChangeTracker interface should exist");
    }

    [Fact]
    public void IChangeTracker_ShouldDefineTrackingMethods()
    {
        var type = Type.GetType("Catga.EventSourcing.IChangeTracker, Catga");

        type.Should().NotBeNull();
        type!.GetMethod("TrackChange").Should().NotBeNull();
        type.GetMethod("GetPendingChangesAsync").Should().NotBeNull();
        type.GetMethod("MarkAsSyncedAsync").Should().NotBeNull();
    }

    #endregion

    #region Read Model Store Tests

    [Fact]
    public void IReadModelStore_ShouldExist()
    {
        var type = Type.GetType("Catga.EventSourcing.IReadModelStore`1, Catga");

        type.Should().NotBeNull("IReadModelStore<T> interface should exist");
    }

    #endregion
}
