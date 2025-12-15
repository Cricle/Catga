using Catga.Abstractions;
using Catga.EventSourcing;
using FluentAssertions;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Concurrency tests for Read Model Sync components
/// </summary>
public class ReadModelSyncConcurrencyTests
{
    private record TestEvent(string Message) : IEvent
    {
        public long MessageId => 0;
    }

    #region InMemoryChangeTracker Concurrency Tests

    [Fact]
    public async Task InMemoryChangeTracker_ConcurrentTrack_ShouldBeThreadSafe()
    {
        var tracker = new InMemoryChangeTracker();
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => tracker.TrackChange(CreateChange($"change-{index}"))));
        }

        await Task.WhenAll(tasks);

        var pending = await tracker.GetPendingChangesAsync();
        pending.Should().HaveCount(100);
    }

    [Fact]
    public async Task InMemoryChangeTracker_ConcurrentTrackAndRead_ShouldBeThreadSafe()
    {
        var tracker = new InMemoryChangeTracker();
        var tasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => tracker.TrackChange(CreateChange($"change-{index}"))));
            tasks.Add(Task.Run(async () => await tracker.GetPendingChangesAsync()));
        }

        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InMemoryChangeTracker_ConcurrentMarkAsSynced_ShouldBeThreadSafe()
    {
        var tracker = new InMemoryChangeTracker();

        for (int i = 0; i < 100; i++)
        {
            tracker.TrackChange(CreateChange($"change-{i}"));
        }

        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () => await tracker.MarkAsSyncedAsync(new[] { $"change-{index}" })));
        }

        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region RealtimeSyncStrategy Concurrency Tests

    [Fact]
    public async Task RealtimeSyncStrategy_ConcurrentExecution_ShouldProcessAll()
    {
        var processedCount = 0;
        var lockObj = new object();
        var strategy = new RealtimeSyncStrategy(_ =>
        {
            lock (lockObj)
            {
                processedCount++;
            }
            return ValueTask.CompletedTask;
        });

        var changes = Enumerable.Range(0, 100).Select(i => CreateChange($"{i}")).ToList();

        var tasks = new List<Task>();
        foreach (var chunk in changes.Chunk(10))
        {
            tasks.Add(Task.Run(async () => await strategy.ExecuteAsync(chunk)));
        }

        await Task.WhenAll(tasks);

        processedCount.Should().Be(100);
    }

    #endregion

    #region BatchSyncStrategy Concurrency Tests

    [Fact]
    public async Task BatchSyncStrategy_ConcurrentExecution_ShouldProcessAll()
    {
        var processedCount = 0;
        var lockObj = new object();
        var strategy = new BatchSyncStrategy(5, batch =>
        {
            lock (lockObj)
            {
                processedCount += batch.Count;
            }
            return ValueTask.CompletedTask;
        });

        var changes = Enumerable.Range(0, 100).Select(i => CreateChange($"{i}")).ToList();

        var tasks = new List<Task>();
        foreach (var chunk in changes.Chunk(10))
        {
            tasks.Add(Task.Run(async () => await strategy.ExecuteAsync(chunk)));
        }

        await Task.WhenAll(tasks);

        processedCount.Should().Be(100);
    }

    #endregion

    #region DefaultReadModelSynchronizer Concurrency Tests

    [Fact]
    public async Task DefaultReadModelSynchronizer_ConcurrentSync_ShouldNotThrow()
    {
        var tracker = new InMemoryChangeTracker();
        var strategy = new RealtimeSyncStrategy(_ => ValueTask.CompletedTask);
        var synchronizer = new DefaultReadModelSynchronizer(tracker, strategy);

        for (int i = 0; i < 50; i++)
        {
            tracker.TrackChange(CreateChange($"change-{i}"));
        }

        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () => await synchronizer.SyncAsync()));
        }

        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task RealtimeSyncStrategy_WithCancellation_ShouldStopProcessing()
    {
        var cts = new CancellationTokenSource();
        var processedCount = 0;
        var strategy = new RealtimeSyncStrategy(c =>
        {
            processedCount++;
            if (processedCount >= 5)
            {
                cts.Cancel();
            }
            return ValueTask.CompletedTask;
        });

        var changes = Enumerable.Range(0, 100).Select(i => CreateChange($"{i}")).ToList();

        var act = async () => await strategy.ExecuteAsync(changes, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        processedCount.Should().BeLessThan(100);
    }

    [Fact]
    public async Task BatchSyncStrategy_WithCancellation_ShouldStopProcessing()
    {
        var cts = new CancellationTokenSource();
        var batchCount = 0;
        var strategy = new BatchSyncStrategy(10, _ =>
        {
            batchCount++;
            if (batchCount >= 2)
            {
                cts.Cancel();
            }
            return ValueTask.CompletedTask;
        });

        var changes = Enumerable.Range(0, 100).Select(i => CreateChange($"{i}")).ToList();

        var act = async () => await strategy.ExecuteAsync(changes, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        batchCount.Should().BeLessThan(10);
    }

    [Fact]
    public async Task InMemoryChangeTracker_GetPendingWithCancellation_ShouldNotThrow()
    {
        var tracker = new InMemoryChangeTracker();
        tracker.TrackChange(CreateChange("1"));

        var cts = new CancellationTokenSource();

        var pending = await tracker.GetPendingChangesAsync(cts.Token);

        pending.Should().HaveCount(1);
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
