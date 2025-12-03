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
        Assert.Equal(10, result.RemainingPermits); // Returns default limit
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
        // CurrentCount now represents available permits (10 - 3 = 7)
        Assert.Equal(7, stats.Value.CurrentCount);
        Assert.Equal(10, stats.Value.Limit);
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
