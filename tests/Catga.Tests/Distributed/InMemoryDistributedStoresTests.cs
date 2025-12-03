using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Idempotency;
using Catga.Persistence.InMemory.Stores;
using Catga.Scheduling;
using Catga.Serialization.MemoryPack;
using MemoryPack;

namespace Catga.Tests.Distributed;

/// <summary>
/// Unit tests for InMemory distributed stores.
/// </summary>
public class InMemoryDistributedStoresTests
{
    #region InMemoryIdempotencyStore Tests

    [Fact]
    public async Task IdempotencyStore_MarkAndCheck_ShouldWork()
    {
        // Arrange
        var serializer = new MemoryPackMessageSerializer();
        var store = new InMemoryIdempotencyStore(serializer);
        const long messageId = 12345L;

        // Act
        var beforeMark = await store.HasBeenProcessedAsync(messageId);
        await store.MarkAsProcessedAsync(messageId, "result");
        var afterMark = await store.HasBeenProcessedAsync(messageId);

        // Assert
        Assert.False(beforeMark);
        Assert.True(afterMark);
    }

    [Fact]
    public async Task IdempotencyStore_GetCachedResult_ShouldReturnStoredResult()
    {
        // Arrange
        var serializer = new MemoryPackMessageSerializer();
        var store = new InMemoryIdempotencyStore(serializer);
        const long messageId = 12345L;
        var expectedResult = new TestResult { Value = 42 };

        // Act
        await store.MarkAsProcessedAsync(messageId, expectedResult);
        var result = await store.GetCachedResultAsync<TestResult>(messageId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42, result.Value);
    }

    #endregion

    #region InMemoryRateLimiter Tests

    [Fact]
    public async Task RateLimiter_TryAcquire_ShouldAcquireWithinLimit()
    {
        // Arrange
        var limiter = new InMemoryRateLimiter(defaultLimit: 10, defaultWindow: TimeSpan.FromMinutes(1));

        // Act
        var result = await limiter.TryAcquireAsync("test-key");

        // Assert
        Assert.True(result.IsAcquired);
        Assert.Equal(9, result.RemainingPermits);
    }

    [Fact]
    public async Task RateLimiter_TryAcquire_ShouldRejectWhenExceedsLimit()
    {
        // Arrange
        var limiter = new InMemoryRateLimiter(defaultLimit: 2, defaultWindow: TimeSpan.FromMinutes(1));

        // Act
        await limiter.TryAcquireAsync("test-key");
        await limiter.TryAcquireAsync("test-key");
        var result = await limiter.TryAcquireAsync("test-key");

        // Assert
        Assert.False(result.IsAcquired);
        Assert.Equal(RateLimitRejectionReason.RateLimitExceeded, result.Reason);
    }

    [Fact]
    public async Task RateLimiter_GetStatistics_ShouldReturnCurrentState()
    {
        // Arrange
        var limiter = new InMemoryRateLimiter(defaultLimit: 10, defaultWindow: TimeSpan.FromMinutes(1));
        await limiter.TryAcquireAsync("test-key", permits: 3);

        // Act
        var stats = await limiter.GetStatisticsAsync("test-key");

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(3, stats.Value.CurrentCount);
        Assert.Equal(10, stats.Value.Limit);
    }

    #endregion

    #region InMemoryLeaderElection Tests

    [Fact]
    public async Task LeaderElection_TryAcquire_ShouldAcquireLeadership()
    {
        // Arrange
        var election = new InMemoryLeaderElection(nodeId: "node-1");

        // Act
        var handle = await election.TryAcquireLeadershipAsync("test-election");

        // Assert
        Assert.NotNull(handle);
        Assert.True(handle.IsLeader);
        Assert.Equal("node-1", handle.NodeId);
    }

    [Fact]
    public async Task LeaderElection_IsLeader_ShouldReturnTrueForLeader()
    {
        // Arrange
        var election = new InMemoryLeaderElection(nodeId: "node-1");
        await election.TryAcquireLeadershipAsync("test-election");

        // Act
        var isLeader = await election.IsLeaderAsync("test-election");

        // Assert
        Assert.True(isLeader);
    }

    [Fact]
    public async Task LeaderElection_GetLeader_ShouldReturnLeaderInfo()
    {
        // Arrange
        var election = new InMemoryLeaderElection(nodeId: "node-1");
        await election.TryAcquireLeadershipAsync("test-election");

        // Act
        var leader = await election.GetLeaderAsync("test-election");

        // Assert
        Assert.NotNull(leader);
        Assert.Equal("node-1", leader.Value.NodeId);
    }

    [Fact]
    public async Task LeaderElection_Release_ShouldReleaseLeadership()
    {
        // Arrange
        var election = new InMemoryLeaderElection(nodeId: "node-1");
        var handle = await election.TryAcquireLeadershipAsync("test-election");

        // Act
        await handle!.DisposeAsync();
        var isLeader = await election.IsLeaderAsync("test-election");

        // Assert
        Assert.False(isLeader);
    }

    #endregion

    #region InMemorySnapshotStore Tests

    [Fact]
    public async Task SnapshotStore_SaveAndLoad_ShouldWork()
    {
        // Arrange
        var store = new InMemorySnapshotStore();
        var aggregate = new TestAggregate { Id = "agg-1", Value = 100 };

        // Act
        await store.SaveAsync("stream-1", aggregate, version: 5);
        var snapshot = await store.LoadAsync<TestAggregate>("stream-1");

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal("stream-1", snapshot.Value.StreamId);
        Assert.Equal(5, snapshot.Value.Version);
        Assert.Equal("agg-1", snapshot.Value.State.Id);
        Assert.Equal(100, snapshot.Value.State.Value);
    }

    [Fact]
    public async Task SnapshotStore_Delete_ShouldRemoveSnapshot()
    {
        // Arrange
        var store = new InMemorySnapshotStore();
        var aggregate = new TestAggregate { Id = "agg-1", Value = 100 };
        await store.SaveAsync("stream-1", aggregate, version: 5);

        // Act
        await store.DeleteAsync("stream-1");
        var snapshot = await store.LoadAsync<TestAggregate>("stream-1");

        // Assert
        Assert.Null(snapshot);
    }

    #endregion

    #region Test Types

    public class TestAggregate
    {
        public string Id { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    #endregion
}

[MemoryPackable]
public partial class TestResult
{
    public int Value { get; set; }
}
