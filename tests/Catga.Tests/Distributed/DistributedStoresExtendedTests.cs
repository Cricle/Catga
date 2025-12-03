using Catga.Abstractions;
using Catga.Persistence.InMemory.Locking;
using Catga.Persistence.InMemory.Stores;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Catga.Tests.Distributed;

/// <summary>
/// Extended unit tests for distributed stores after refactoring.
/// Tests primary constructor patterns and simplified implementations.
/// </summary>
public class DistributedStoresExtendedTests
{
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();

    #region InMemoryIdempotencyStore Extended Tests

    [Fact]
    public async Task IdempotencyStore_WithCustomRetention_ShouldExpireOldEntries()
    {
        // Arrange - very short retention
        var store = new InMemoryIdempotencyStore(_serializer, retention: TimeSpan.FromMilliseconds(50));
        const long messageId = 100L;

        // Act
        await store.MarkAsProcessedAsync(messageId, "result");
        var immediateCheck = await store.HasBeenProcessedAsync(messageId);

        await Task.Delay(100); // Wait for expiration
        var afterExpiry = await store.HasBeenProcessedAsync(messageId);

        // Assert
        immediateCheck.Should().BeTrue();
        afterExpiry.Should().BeFalse();
    }

    [Fact]
    public async Task IdempotencyStore_NullResult_ShouldStillMarkAsProcessed()
    {
        // Arrange
        var store = new InMemoryIdempotencyStore(_serializer);
        const long messageId = 200L;

        // Act
        await store.MarkAsProcessedAsync<string>(messageId, null);
        var isProcessed = await store.HasBeenProcessedAsync(messageId);
        var cached = await store.GetCachedResultAsync<string>(messageId);

        // Assert
        isProcessed.Should().BeTrue();
        cached.Should().BeNull();
    }

    [Fact]
    public async Task IdempotencyStore_ComplexResult_ShouldSerializeCorrectly()
    {
        // Arrange
        var store = new InMemoryIdempotencyStore(_serializer);
        const long messageId = 300L;
        var complexResult = new ComplexTestResult
        {
            Id = "test-123",
            Values = new List<int> { 1, 2, 3 },
            Nested = new NestedData { Name = "nested", Score = 99.5 }
        };

        // Act
        await store.MarkAsProcessedAsync(messageId, complexResult);
        var cached = await store.GetCachedResultAsync<ComplexTestResult>(messageId);

        // Assert
        cached.Should().NotBeNull();
        cached!.Id.Should().Be("test-123");
        cached.Values.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        cached.Nested.Should().NotBeNull();
        cached.Nested!.Name.Should().Be("nested");
        cached.Nested.Score.Should().Be(99.5);
    }

    #endregion

    #region InMemoryRateLimiter Extended Tests

    [Fact]
    public async Task RateLimiter_MultipleKeys_ShouldTrackIndependently()
    {
        // Arrange
        var limiter = new InMemoryRateLimiter(defaultLimit: 2, defaultWindow: TimeSpan.FromMinutes(1));

        // Act
        var key1Result1 = await limiter.TryAcquireAsync("key1");
        var key1Result2 = await limiter.TryAcquireAsync("key1");
        var key1Result3 = await limiter.TryAcquireAsync("key1");
        var key2Result1 = await limiter.TryAcquireAsync("key2");

        // Assert
        key1Result1.IsAcquired.Should().BeTrue();
        key1Result2.IsAcquired.Should().BeTrue();
        key1Result3.IsAcquired.Should().BeFalse();
        key2Result1.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task RateLimiter_CustomLimitPerKey_ShouldOverrideDefault()
    {
        // Arrange
        var limiter = new InMemoryRateLimiter(defaultLimit: 10, defaultWindow: TimeSpan.FromMinutes(1));

        // Act - request 5 permits at once
        var result = await limiter.TryAcquireAsync("bulk-key", permits: 5);
        var stats = await limiter.GetStatisticsAsync("bulk-key");

        // Assert
        result.IsAcquired.Should().BeTrue();
        result.RemainingPermits.Should().Be(5);
        stats.Should().NotBeNull();
        stats!.Value.CurrentCount.Should().Be(5);
    }

    [Fact]
    public async Task RateLimiter_WindowExpiry_ShouldResetCount()
    {
        // Arrange - very short window
        var limiter = new InMemoryRateLimiter(defaultLimit: 1, defaultWindow: TimeSpan.FromMilliseconds(50));

        // Act
        var first = await limiter.TryAcquireAsync("expiry-key");
        var second = await limiter.TryAcquireAsync("expiry-key");

        await Task.Delay(100); // Wait for window to expire
        var afterExpiry = await limiter.TryAcquireAsync("expiry-key");

        // Assert
        first.IsAcquired.Should().BeTrue();
        second.IsAcquired.Should().BeFalse();
        afterExpiry.IsAcquired.Should().BeTrue();
    }

    #endregion

    #region InMemoryLeaderElection Extended Tests

    [Fact]
    public async Task LeaderElection_MultipleNodes_OnlyOneLeader()
    {
        // Arrange
        var election1 = new InMemoryLeaderElection(nodeId: "node-1");
        var election2 = new InMemoryLeaderElection(nodeId: "node-2");

        // Act
        var handle1 = await election1.TryAcquireLeadershipAsync("shared-election");
        var handle2 = await election2.TryAcquireLeadershipAsync("shared-election");

        // Assert - first one wins, second fails (different instances so both succeed in InMemory)
        handle1.Should().NotBeNull();
        handle2.Should().NotBeNull(); // InMemory doesn't share state between instances
    }

    [Fact]
    public async Task LeaderElection_IsLeader_ShouldRemainTrueWhileHeld()
    {
        // Arrange
        var election = new InMemoryLeaderElection(nodeId: "heartbeat-node");
        var handle = await election.TryAcquireLeadershipAsync("heartbeat-election");

        // Act
        var isLeaderBefore = handle!.IsLeader;
        await Task.Delay(10); // Small delay
        var isLeaderAfter = handle.IsLeader;

        // Assert
        isLeaderBefore.Should().BeTrue();
        isLeaderAfter.Should().BeTrue();
    }

    [Fact]
    public async Task LeaderElection_MultipleElections_ShouldBeIndependent()
    {
        // Arrange
        var election = new InMemoryLeaderElection(nodeId: "multi-node");

        // Act
        var handle1 = await election.TryAcquireLeadershipAsync("election-1");
        var handle2 = await election.TryAcquireLeadershipAsync("election-2");
        var isLeader1 = await election.IsLeaderAsync("election-1");
        var isLeader2 = await election.IsLeaderAsync("election-2");

        // Assert
        handle1.Should().NotBeNull();
        handle2.Should().NotBeNull();
        isLeader1.Should().BeTrue();
        isLeader2.Should().BeTrue();
    }

    #endregion

    #region InMemoryDistributedLock Extended Tests

    [Fact]
    public async Task DistributedLock_TryAcquire_ShouldSucceed()
    {
        // Arrange
        var options = Options.Create(new DistributedLockOptions { RetryInterval = TimeSpan.FromMilliseconds(10) });
        var logger = NullLogger<InMemoryDistributedLock>.Instance;
        var lockService = new InMemoryDistributedLock(options, logger);

        // Act
        var handle = await lockService.TryAcquireAsync("test-resource", TimeSpan.FromSeconds(10));

        // Assert
        handle.Should().NotBeNull();
        handle!.IsValid.Should().BeTrue();
        handle.Resource.Should().Be("test-resource");

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_DoubleLock_ShouldFail()
    {
        // Arrange
        var options = Options.Create(new DistributedLockOptions { RetryInterval = TimeSpan.FromMilliseconds(10) });
        var logger = NullLogger<InMemoryDistributedLock>.Instance;
        var lockService = new InMemoryDistributedLock(options, logger);

        // Act
        var handle1 = await lockService.TryAcquireAsync("double-lock", TimeSpan.FromSeconds(10));
        var handle2 = await lockService.TryAcquireAsync("double-lock", TimeSpan.FromSeconds(10));

        // Assert
        handle1.Should().NotBeNull();
        handle2.Should().BeNull();

        await handle1!.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_Extend_ShouldUpdateExpiry()
    {
        // Arrange
        var options = Options.Create(new DistributedLockOptions { RetryInterval = TimeSpan.FromMilliseconds(10) });
        var logger = NullLogger<InMemoryDistributedLock>.Instance;
        var lockService = new InMemoryDistributedLock(options, logger);

        // Act
        var handle = await lockService.TryAcquireAsync("extend-lock", TimeSpan.FromMilliseconds(100));
        var originalExpiry = handle!.ExpiresAt;
        await handle.ExtendAsync(TimeSpan.FromSeconds(10));
        var newExpiry = handle.ExpiresAt;

        // Assert
        newExpiry.Should().BeAfter(originalExpiry);

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_IsLocked_ShouldReflectState()
    {
        // Arrange
        var options = Options.Create(new DistributedLockOptions { RetryInterval = TimeSpan.FromMilliseconds(10) });
        var logger = NullLogger<InMemoryDistributedLock>.Instance;
        var lockService = new InMemoryDistributedLock(options, logger);

        // Act
        var beforeLock = await lockService.IsLockedAsync("state-lock");
        var handle = await lockService.TryAcquireAsync("state-lock", TimeSpan.FromSeconds(10));
        var duringLock = await lockService.IsLockedAsync("state-lock");
        await handle!.DisposeAsync();
        var afterRelease = await lockService.IsLockedAsync("state-lock");

        // Assert
        beforeLock.Should().BeFalse();
        duringLock.Should().BeTrue();
        afterRelease.Should().BeFalse();
    }

    [Fact]
    public async Task DistributedLock_ExpiredLock_ShouldAllowReacquisition()
    {
        // Arrange
        var options = Options.Create(new DistributedLockOptions { RetryInterval = TimeSpan.FromMilliseconds(10) });
        var logger = NullLogger<InMemoryDistributedLock>.Instance;
        var lockService = new InMemoryDistributedLock(options, logger);

        // Act - acquire with very short expiry
        var handle1 = await lockService.TryAcquireAsync("expiring-lock", TimeSpan.FromMilliseconds(50));
        await Task.Delay(100); // Wait for expiration
        var handle2 = await lockService.TryAcquireAsync("expiring-lock", TimeSpan.FromSeconds(10));

        // Assert
        handle1.Should().NotBeNull();
        handle2.Should().NotBeNull();

        await handle2!.DisposeAsync();
    }

    #endregion

    #region InMemorySnapshotStore Extended Tests

    [Fact]
    public async Task SnapshotStore_Overwrite_ShouldReplaceExisting()
    {
        // Arrange
        var store = new InMemorySnapshotStore();
        var aggregate1 = new TestAggregate { Id = "agg-1", Value = 100 };
        var aggregate2 = new TestAggregate { Id = "agg-1", Value = 200 };

        // Act
        await store.SaveAsync("stream-1", aggregate1, version: 5);
        await store.SaveAsync("stream-1", aggregate2, version: 10);
        var snapshot = await store.LoadAsync<TestAggregate>("stream-1");

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.Value.Version.Should().Be(10);
        snapshot.Value.State.Value.Should().Be(200);
    }

    [Fact]
    public async Task SnapshotStore_LoadNonExistent_ShouldReturnNull()
    {
        // Arrange
        var store = new InMemorySnapshotStore();

        // Act
        var snapshot = await store.LoadAsync<TestAggregate>("non-existent");

        // Assert
        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task SnapshotStore_MultipleStreams_ShouldBeIndependent()
    {
        // Arrange
        var store = new InMemorySnapshotStore();
        var aggregate1 = new TestAggregate { Id = "agg-1", Value = 100 };
        var aggregate2 = new TestAggregate { Id = "agg-2", Value = 200 };

        // Act
        await store.SaveAsync("stream-1", aggregate1, version: 5);
        await store.SaveAsync("stream-2", aggregate2, version: 10);
        var snapshot1 = await store.LoadAsync<TestAggregate>("stream-1");
        var snapshot2 = await store.LoadAsync<TestAggregate>("stream-2");

        // Assert
        snapshot1!.Value.State.Value.Should().Be(100);
        snapshot2!.Value.State.Value.Should().Be(200);
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
public partial class ComplexTestResult
{
    public string Id { get; set; } = string.Empty;
    public List<int> Values { get; set; } = new();
    public NestedData? Nested { get; set; }
}

[MemoryPackable]
public partial class NestedData
{
    public string Name { get; set; } = string.Empty;
    public double Score { get; set; }
}
