using Catga.Abstractions;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for Cluster features.
/// Tests distributed locks, rate limiting, leader election, and cluster coordination.
/// </summary>
public class ClusterFeaturesE2ETests
{
    [Fact]
    public async Task DistributedLock_AcquireAndRelease_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var lockManager = sp.GetRequiredService<IDistributedLockManager>();

        var lockKey = $"test-lock-{Guid.NewGuid():N}";

        // Act - Acquire lock
        var lockHandle = await lockManager.AcquireAsync(lockKey, TimeSpan.FromSeconds(30));

        // Assert
        lockHandle.Should().NotBeNull();
        lockHandle!.Key.Should().Be(lockKey);
        lockHandle.IsAcquired.Should().BeTrue();

        // Act - Release lock
        await lockHandle.ReleaseAsync();

        // Assert - Can acquire again
        var newHandle = await lockManager.AcquireAsync(lockKey, TimeSpan.FromSeconds(30));
        newHandle.Should().NotBeNull();
        await newHandle!.ReleaseAsync();
    }

    [Fact]
    public async Task DistributedLock_ConcurrentAcquire_OnlyOneSucceeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var lockManager = sp.GetRequiredService<IDistributedLockManager>();

        var lockKey = $"concurrent-lock-{Guid.NewGuid():N}";
        var acquiredCount = 0;

        // Act - First acquire
        var handle1 = await lockManager.AcquireAsync(lockKey, TimeSpan.FromSeconds(30));
        if (handle1 != null) Interlocked.Increment(ref acquiredCount);

        // Act - Second acquire (should fail or wait)
        var handle2 = await lockManager.TryAcquireAsync(lockKey, TimeSpan.FromMilliseconds(100));
        if (handle2 != null) Interlocked.Increment(ref acquiredCount);

        // Assert
        handle1.Should().NotBeNull();
        handle2.Should().BeNull(); // Should not acquire while handle1 holds lock
        acquiredCount.Should().Be(1);

        // Cleanup
        await handle1!.ReleaseAsync();
    }

    [Fact]
    public async Task RateLimiter_WithinLimit_Allows()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var rateLimiter = sp.GetRequiredService<IRateLimiter>();

        var key = $"rate-limit-{Guid.NewGuid():N}";
        var limit = 5;
        var window = TimeSpan.FromSeconds(10);

        // Act - Make requests within limit
        var results = new List<bool>();
        for (int i = 0; i < limit; i++)
        {
            var allowed = await rateLimiter.IsAllowedAsync(key, limit, window);
            results.Add(allowed);
        }

        // Assert
        results.Should().AllSatisfy(r => r.Should().BeTrue());
    }

    [Fact]
    public async Task RateLimiter_ExceedsLimit_Denies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var rateLimiter = sp.GetRequiredService<IRateLimiter>();

        var key = $"rate-exceed-{Guid.NewGuid():N}";
        var limit = 3;
        var window = TimeSpan.FromSeconds(10);

        // Act - Exceed limit
        var results = new List<bool>();
        for (int i = 0; i < limit + 2; i++)
        {
            var allowed = await rateLimiter.IsAllowedAsync(key, limit, window);
            results.Add(allowed);
        }

        // Assert - First 3 allowed, rest denied
        results.Take(limit).Should().AllSatisfy(r => r.Should().BeTrue());
        results.Skip(limit).Should().AllSatisfy(r => r.Should().BeFalse());
    }

    [Fact]
    public async Task LeaderElection_SingleNode_BecomesLeader()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var leaderElection = sp.GetRequiredService<ILeaderElection>();

        var electionKey = $"election-{Guid.NewGuid():N}";
        var nodeId = "node-1";

        // Act
        var isLeader = await leaderElection.TryBecomeLeaderAsync(electionKey, nodeId, TimeSpan.FromMinutes(1));

        // Assert
        isLeader.Should().BeTrue();

        // Verify leadership
        var currentLeader = await leaderElection.GetCurrentLeaderAsync(electionKey);
        currentLeader.Should().Be(nodeId);
    }

    [Fact]
    public async Task LeaderElection_MultipleNodes_OnlyOneLeader()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var leaderElection = sp.GetRequiredService<ILeaderElection>();

        var electionKey = $"multi-election-{Guid.NewGuid():N}";

        // Act - Multiple nodes try to become leader
        var node1Leader = await leaderElection.TryBecomeLeaderAsync(electionKey, "node-1", TimeSpan.FromMinutes(1));
        var node2Leader = await leaderElection.TryBecomeLeaderAsync(electionKey, "node-2", TimeSpan.FromMinutes(1));
        var node3Leader = await leaderElection.TryBecomeLeaderAsync(electionKey, "node-3", TimeSpan.FromMinutes(1));

        // Assert - Only one should be leader
        var leaderCount = new[] { node1Leader, node2Leader, node3Leader }.Count(x => x);
        leaderCount.Should().Be(1);
    }

    [Fact]
    public async Task Idempotency_DuplicateRequest_ReturnsStoredResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var idempotencyStore = sp.GetRequiredService<IIdempotencyStore>();

        var requestId = $"request-{Guid.NewGuid():N}";
        var result = new { OrderId = "ORD-001", Status = "Created" };

        // Act - Store result
        await idempotencyStore.StoreResultAsync(requestId, result, TimeSpan.FromHours(1));

        // Act - Check if processed
        var isProcessed = await idempotencyStore.IsProcessedAsync(requestId);
        var storedResult = await idempotencyStore.GetResultAsync<object>(requestId);

        // Assert
        isProcessed.Should().BeTrue();
        storedResult.Should().NotBeNull();
    }

    [Fact]
    public async Task DeadLetterQueue_StoreAndRetrieve_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlq = sp.GetRequiredService<IDeadLetterQueue>();

        var deadLetter = new DeadLetter(
            "test-message",
            "Failed to process",
            DateTime.UtcNow,
            new Dictionary<string, object> { ["correlationId"] = "COR-001" });

        // Act - Store dead letter
        await dlq.StoreAsync(deadLetter);

        // Act - Retrieve dead letters
        var retrieved = await dlq.GetAllAsync(10);

        // Assert
        retrieved.Should().NotBeEmpty();
        retrieved.Should().Contain(d => d.MessageId == "test-message");
    }

    [Fact]
    public async Task Outbox_StoreAndMarkAsProcessed_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var outbox = sp.GetRequiredService<IOutbox>();

        var outboxMessage = new OutboxMessage(
            $"outbox-{Guid.NewGuid():N}",
            "OrderCreated",
            new byte[] { 1, 2, 3 },
            DateTime.UtcNow);

        // Act - Store message
        await outbox.StoreAsync(outboxMessage);

        // Act - Get pending
        var pending = await outbox.GetPendingAsync(10);
        pending.Should().NotBeEmpty();

        // Act - Mark as processed
        await outbox.MarkAsProcessedAsync(outboxMessage.MessageId);

        // Act - Get pending again
        var pendingAfter = await outbox.GetPendingAsync(10);
        pendingAfter.Should().NotContain(m => m.MessageId == outboxMessage.MessageId);
    }

    [Fact]
    public async Task Inbox_CheckDuplicates_PreventsDuplicateProcessing()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var inbox = sp.GetRequiredService<IInbox>();

        var messageId = $"inbox-{Guid.NewGuid():N}";

        // Act - First check (not processed)
        var firstCheck = await inbox.IsProcessedAsync(messageId);
        firstCheck.Should().BeFalse();

        // Act - Mark as processed
        await inbox.MarkAsProcessedAsync(messageId);

        // Act - Second check (processed)
        var secondCheck = await inbox.IsProcessedAsync(messageId);
        secondCheck.Should().BeTrue();
    }
}
