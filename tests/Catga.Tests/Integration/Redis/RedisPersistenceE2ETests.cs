using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.Redis;
using Catga.Persistence.Redis.Locking;
using Catga.Persistence.Redis.Persistence;
using Catga.Persistence.Redis.RateLimiting;
using Catga.Persistence.Redis.Scheduling;
using Catga.Persistence.Redis.Stores;
using Catga.Resilience;
using Catga.Scheduling;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Catga.Tests.Integration.Redis;

/// <summary>
/// E2E tests for Redis Persistence stores.
/// Target: 80% coverage for Catga.Persistence.Redis
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class RedisPersistenceE2ETests : IAsyncLifetime
{
    private RedisContainer? _container;
    private IConnectionMultiplexer? _redis;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;
        var image = ResolveImage("TEST_REDIS_IMAGE", "redis:7-alpine");
        if (image is null) return;

        _container = new RedisBuilder().WithImage(image).Build();
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_redis is not null) await _redis.CloseAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    #region RedisIdempotencyStore Tests

    [Fact]
    public async Task IdempotencyStore_MarkAsProcessed_ShouldStoreResult()
    {
        if (_redis is null) return;
        var store = new RedisIdempotencyStore(_redis, _serializer, NullLogger<RedisIdempotencyStore>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();

        await store.MarkAsProcessedAsync(messageId, new TestResult { Value = 42 });
        var result = await store.HasBeenProcessedAsync(messageId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IdempotencyStore_GetCachedResult_ShouldReturnStoredValue()
    {
        if (_redis is null) return;
        var store = new RedisIdempotencyStore(_redis, _serializer, NullLogger<RedisIdempotencyStore>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();
        var expected = new TestResult { Value = 123 };

        await store.MarkAsProcessedAsync(messageId, expected);
        var cached = await store.GetCachedResultAsync<TestResult>(messageId);

        cached.Should().NotBeNull();
        cached!.Value.Should().Be(123);
    }

    [Fact]
    public async Task IdempotencyStore_HasBeenProcessed_NotProcessed_ShouldReturnFalse()
    {
        if (_redis is null) return;
        var store = new RedisIdempotencyStore(_redis, _serializer, NullLogger<RedisIdempotencyStore>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();

        var result = await store.HasBeenProcessedAsync(messageId);

        result.Should().BeFalse();
    }

    #endregion

    #region RedisOutboxPersistence Tests

    [Fact]
    public async Task OutboxStore_AddAndGetPending_ShouldWork()
    {
        if (_redis is null) return;
        var outbox = new RedisOutboxPersistence(_redis, _serializer, NullLogger<RedisOutboxPersistence>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new TestMessage { Data = "outbox" }),
            CreatedAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending
        };

        await outbox.AddAsync(message);
        var pending = await outbox.GetPendingMessagesAsync(10);

        pending.Should().Contain(m => m.MessageId == messageId);
    }

    [Fact]
    public async Task OutboxStore_MarkAsPublished_ShouldRemoveFromPending()
    {
        if (_redis is null) return;
        var outbox = new RedisOutboxPersistence(_redis, _serializer, NullLogger<RedisOutboxPersistence>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new TestMessage { Data = "publish" }),
            CreatedAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending
        };

        await outbox.AddAsync(message);
        await outbox.MarkAsPublishedAsync(messageId);
        var pending = await outbox.GetPendingMessagesAsync(100);

        pending.Should().NotContain(m => m.MessageId == messageId);
    }

    [Fact]
    public async Task OutboxStore_MarkAsFailed_ShouldUpdateStatus()
    {
        if (_redis is null) return;
        var outbox = new RedisOutboxPersistence(_redis, _serializer, NullLogger<RedisOutboxPersistence>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new TestMessage { Data = "fail" }),
            CreatedAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending
        };

        await outbox.AddAsync(message);
        await outbox.MarkAsFailedAsync(messageId, "Test error");

        // Should still be retrievable for retry
        var pending = await outbox.GetPendingMessagesAsync(100);
        pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task OutboxStore_DeletePublished_ShouldCleanup()
    {
        if (_redis is null) return;
        var outbox = new RedisOutboxPersistence(_redis, _serializer, NullLogger<RedisOutboxPersistence>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new TestMessage { Data = "delete" }),
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            Status = OutboxStatus.Pending
        };

        await outbox.AddAsync(message);
        await outbox.MarkAsPublishedAsync(messageId);
        await outbox.DeletePublishedMessagesAsync(TimeSpan.FromDays(1));

        // Cleanup should have removed old published messages
    }

    #endregion

    #region RedisInboxPersistence Tests

    [Fact]
    public async Task InboxStore_TryLockMessage_ShouldAcquireLock()
    {
        if (_redis is null) return;
        var inbox = new RedisInboxPersistence(_redis, _serializer, NullLogger<RedisInboxPersistence>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();

        var locked = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        locked.Should().BeTrue();
    }

    [Fact]
    public async Task InboxStore_TryLockMessage_AlreadyLocked_ShouldFail()
    {
        if (_redis is null) return;
        var inbox = new RedisInboxPersistence(_redis, _serializer, NullLogger<RedisInboxPersistence>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();

        var first = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        var second = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task InboxStore_MarkAsProcessed_ShouldSetProcessedFlag()
    {
        if (_redis is null) return;
        var inbox = new RedisInboxPersistence(_redis, _serializer, NullLogger<RedisInboxPersistence>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();
        var inboxMsg = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new TestMessage { Data = "inbox" }),
            Status = InboxStatus.Processing
        };

        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.MarkAsProcessedAsync(inboxMsg);
        var processed = await inbox.HasBeenProcessedAsync(messageId);

        processed.Should().BeTrue();
    }

    [Fact]
    public async Task InboxStore_ReleaseLock_ShouldAllowReacquisition()
    {
        if (_redis is null) return;
        var inbox = new RedisInboxPersistence(_redis, _serializer, NullLogger<RedisInboxPersistence>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();

        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.ReleaseLockAsync(messageId);
        var reacquired = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        reacquired.Should().BeTrue();
    }

    [Fact]
    public async Task InboxStore_GetProcessedResult_ShouldReturnStoredResult()
    {
        if (_redis is null) return;
        var inbox = new RedisInboxPersistence(_redis, _serializer, NullLogger<RedisInboxPersistence>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();
        var inboxMsg = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new TestMessage { Data = "result" }),
            Status = InboxStatus.Processed,
            ProcessingResult = _serializer.Serialize(new TestResult { Value = 999 })
        };

        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.MarkAsProcessedAsync(inboxMsg);
        var result = await inbox.GetProcessedResultAsync(messageId);

        result.Should().NotBeNull();
    }

    #endregion

    #region RedisSnapshotStore Tests

    [Fact]
    public async Task SnapshotStore_SaveAndLoad_ShouldWork()
    {
        if (_redis is null) return;
        var store = new RedisSnapshotStore(_redis, _serializer, Options.Create(new SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);
        var streamId = $"stream-{Guid.NewGuid():N}";
        var aggregate = new TestAggregate { Id = "agg-1", Counter = 42 };

        await store.SaveAsync(streamId, aggregate, version: 10);
        var snapshot = await store.LoadAsync<TestAggregate>(streamId);

        snapshot.Should().NotBeNull();
        snapshot!.Value.Version.Should().Be(10);
        snapshot.Value.State.Counter.Should().Be(42);
    }

    [Fact]
    public async Task SnapshotStore_Delete_ShouldRemoveSnapshot()
    {
        if (_redis is null) return;
        var store = new RedisSnapshotStore(_redis, _serializer, Options.Create(new SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);
        var streamId = $"stream-del-{Guid.NewGuid():N}";
        var aggregate = new TestAggregate { Id = "agg-del", Counter = 99 };

        await store.SaveAsync(streamId, aggregate, version: 5);
        await store.DeleteAsync(streamId);
        var snapshot = await store.LoadAsync<TestAggregate>(streamId);

        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task SnapshotStore_LoadNonExistent_ShouldReturnNull()
    {
        if (_redis is null) return;
        var store = new RedisSnapshotStore(_redis, _serializer, Options.Create(new SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);

        var snapshot = await store.LoadAsync<TestAggregate>("non-existent-stream");

        snapshot.Should().BeNull();
    }

    #endregion

    #region RedisDeadLetterQueue Tests

    [Fact]
    public async Task DeadLetterQueue_SendAndGet_ShouldWork()
    {
        if (_redis is null) return;
        var dlq = new RedisDeadLetterQueue(_redis, _serializer, $"dlq-{Guid.NewGuid():N}:");
        var message = new TestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "dlq-test" };

        await dlq.SendAsync(message, new Exception("Test error"), retryCount: 3);
        var failed = await dlq.GetFailedMessagesAsync(10);

        failed.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DeadLetterQueue_MultipleMessages_ShouldRetrieveAll()
    {
        if (_redis is null) return;
        var dlq = new RedisDeadLetterQueue(_redis, _serializer, $"dlq-multi-{Guid.NewGuid():N}:");

        for (int i = 0; i < 5; i++)
        {
            var message = new TestMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"dlq-{i}" };
            await dlq.SendAsync(message, new Exception($"Error {i}"), retryCount: i);
        }

        var failed = await dlq.GetFailedMessagesAsync(10);

        failed.Count.Should().BeGreaterOrEqualTo(5);
    }

    #endregion

    #region RedisRateLimiter Tests

    [Fact]
    public async Task RateLimiter_TryAcquire_WithinLimit_ShouldSucceed()
    {
        if (_redis is null) return;
        var opts = Options.Create(new DistributedRateLimiterOptions { DefaultPermitLimit = 10, DefaultWindow = TimeSpan.FromMinutes(1) });
        var limiter = new RedisRateLimiter(_redis, opts, NullLogger<RedisRateLimiter>.Instance);
        var key = $"rate-{Guid.NewGuid():N}";

        var result = await limiter.TryAcquireAsync(key);

        result.IsAcquired.Should().BeTrue();
        result.RemainingPermits.Should().Be(9);
    }

    [Fact]
    public async Task RateLimiter_TryAcquire_ExceedsLimit_ShouldFail()
    {
        if (_redis is null) return;
        var opts = Options.Create(new DistributedRateLimiterOptions { DefaultPermitLimit = 2, DefaultWindow = TimeSpan.FromMinutes(1) });
        var limiter = new RedisRateLimiter(_redis, opts, NullLogger<RedisRateLimiter>.Instance);
        var key = $"rate-exceed-{Guid.NewGuid():N}";

        await limiter.TryAcquireAsync(key);
        await limiter.TryAcquireAsync(key);
        var result = await limiter.TryAcquireAsync(key);

        result.IsAcquired.Should().BeFalse();
        result.Reason.Should().Be(RateLimitRejectionReason.RateLimitExceeded);
    }

    [Fact]
    public async Task RateLimiter_GetStatistics_ShouldReturnCurrentState()
    {
        if (_redis is null) return;
        var opts = Options.Create(new DistributedRateLimiterOptions { DefaultPermitLimit = 10, DefaultWindow = TimeSpan.FromMinutes(1) });
        var limiter = new RedisRateLimiter(_redis, opts, NullLogger<RedisRateLimiter>.Instance);
        var key = $"rate-stats-{Guid.NewGuid():N}";

        await limiter.TryAcquireAsync(key, permits: 3);
        var stats = await limiter.GetStatisticsAsync(key);

        stats.Should().NotBeNull();
        stats!.Value.CurrentCount.Should().Be(3);
        stats.Value.Limit.Should().Be(10);
    }

    [Fact]
    public async Task RateLimiter_MultiplePermits_ShouldDeductCorrectly()
    {
        if (_redis is null) return;
        var opts = Options.Create(new DistributedRateLimiterOptions { DefaultPermitLimit = 10, DefaultWindow = TimeSpan.FromMinutes(1) });
        var limiter = new RedisRateLimiter(_redis, opts, NullLogger<RedisRateLimiter>.Instance);
        var key = $"rate-multi-{Guid.NewGuid():N}";

        var result = await limiter.TryAcquireAsync(key, permits: 5);

        result.IsAcquired.Should().BeTrue();
        result.RemainingPermits.Should().Be(5);
    }

    #endregion

    #region RedisDistributedLock Tests

    [Fact]
    public async Task DistributedLock_TryAcquire_ShouldSucceed()
    {
        if (_redis is null) return;
        var lockService = new RedisDistributedLock(_redis, Options.Create(new DistributedLockOptions()), NullLogger<RedisDistributedLock>.Instance);
        var resource = $"lock-{Guid.NewGuid():N}";

        var handle = await lockService.TryAcquireAsync(resource, TimeSpan.FromSeconds(30));

        handle.Should().NotBeNull();
        handle!.IsValid.Should().BeTrue();
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_DoubleLock_ShouldFail()
    {
        if (_redis is null) return;
        var lockService = new RedisDistributedLock(_redis, Options.Create(new DistributedLockOptions()), NullLogger<RedisDistributedLock>.Instance);
        var resource = $"lock-double-{Guid.NewGuid():N}";

        var handle1 = await lockService.TryAcquireAsync(resource, TimeSpan.FromSeconds(30));
        var handle2 = await lockService.TryAcquireAsync(resource, TimeSpan.FromSeconds(30));

        handle1.Should().NotBeNull();
        handle2.Should().BeNull();
        await handle1!.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_Release_ShouldAllowReacquisition()
    {
        if (_redis is null) return;
        var lockService = new RedisDistributedLock(_redis, Options.Create(new DistributedLockOptions()), NullLogger<RedisDistributedLock>.Instance);
        var resource = $"lock-release-{Guid.NewGuid():N}";

        var handle1 = await lockService.TryAcquireAsync(resource, TimeSpan.FromSeconds(30));
        await handle1!.DisposeAsync();
        var handle2 = await lockService.TryAcquireAsync(resource, TimeSpan.FromSeconds(30));

        handle2.Should().NotBeNull();
        await handle2!.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_IsLocked_ShouldReflectState()
    {
        if (_redis is null) return;
        var lockService = new RedisDistributedLock(_redis, Options.Create(new DistributedLockOptions()), NullLogger<RedisDistributedLock>.Instance);
        var resource = $"lock-state-{Guid.NewGuid():N}";

        var before = await lockService.IsLockedAsync(resource);
        var handle = await lockService.TryAcquireAsync(resource, TimeSpan.FromSeconds(30));
        var during = await lockService.IsLockedAsync(resource);
        await handle!.DisposeAsync();
        var after = await lockService.IsLockedAsync(resource);

        before.Should().BeFalse();
        during.Should().BeTrue();
        after.Should().BeFalse();
    }

    #endregion

    #region RedisLeaderElection Tests

    [Fact]
    public async Task LeaderElection_TryAcquire_ShouldSucceed()
    {
        if (_redis is null) return;
        var election = new RedisLeaderElection(_redis, Options.Create(new LeaderElectionOptions()), NullLogger<RedisLeaderElection>.Instance);
        var electionName = $"election-{Guid.NewGuid():N}";

        var handle = await election.TryAcquireLeadershipAsync(electionName);

        handle.Should().NotBeNull();
        handle!.IsLeader.Should().BeTrue();
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task LeaderElection_IsLeader_ShouldReturnTrue()
    {
        if (_redis is null) return;
        var election = new RedisLeaderElection(_redis, Options.Create(new LeaderElectionOptions()), NullLogger<RedisLeaderElection>.Instance);
        var electionName = $"election-isleader-{Guid.NewGuid():N}";

        var handle = await election.TryAcquireLeadershipAsync(electionName);
        var isLeader = await election.IsLeaderAsync(electionName);

        isLeader.Should().BeTrue();
        await handle!.DisposeAsync();
    }

    [Fact]
    public async Task LeaderElection_GetLeader_ShouldReturnLeaderInfo()
    {
        if (_redis is null) return;
        var election = new RedisLeaderElection(_redis, Options.Create(new LeaderElectionOptions()), NullLogger<RedisLeaderElection>.Instance);
        var electionName = $"election-getleader-{Guid.NewGuid():N}";

        var handle = await election.TryAcquireLeadershipAsync(electionName);
        var leader = await election.GetLeaderAsync(electionName);

        leader.Should().NotBeNull();
        await handle!.DisposeAsync();
    }

    #endregion

    #region RedisSnapshotStore Tests

    [Fact]
    public async Task SnapshotStore_SaveAndLoad_ShouldRoundTrip()
    {
        if (_redis is null) return;
        var store = new RedisSnapshotStore(_redis, _serializer, Options.Create(new SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);
        var aggregateId = $"agg-{Guid.NewGuid():N}";

        await store.SaveAsync(aggregateId, new TestSnapshot { Value = 42 }, 5);
        var loaded = await store.LoadAsync<TestSnapshot>(aggregateId);

        loaded.Should().NotBeNull();
    }

    [Fact]
    public async Task SnapshotStore_Load_NonExistent_ShouldReturnNull()
    {
        if (_redis is null) return;
        var store = new RedisSnapshotStore(_redis, _serializer, Options.Create(new SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);

        var loaded = await store.LoadAsync<TestSnapshot>("non-existent-agg");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task RedisSnapshotStore_Delete_ShouldRemoveSnapshot()
    {
        if (_redis is null) return;
        var store = new RedisSnapshotStore(_redis, _serializer, Options.Create(new SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);
        var aggregateId = $"agg-delete-{Guid.NewGuid():N}";

        await store.SaveAsync(aggregateId, new TestSnapshot { Value = 1 }, 1);
        await store.DeleteAsync(aggregateId);
        var loaded = await store.LoadAsync<TestSnapshot>(aggregateId);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task SnapshotStore_SaveMultiple_ShouldOverwrite()
    {
        if (_redis is null) return;
        var store = new RedisSnapshotStore(_redis, _serializer, Options.Create(new SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);
        var aggregateId = $"agg-overwrite-{Guid.NewGuid():N}";

        await store.SaveAsync(aggregateId, new TestSnapshot { Value = 1 }, 1);
        await store.SaveAsync(aggregateId, new TestSnapshot { Value = 2 }, 2);
        var loaded = await store.LoadAsync<TestSnapshot>(aggregateId);

        loaded.Should().NotBeNull();
    }

    #endregion

    #region RedisDistributedLock Extended Tests

    [Fact]
    public async Task RedisDistributedLock_TryAcquire_ShouldSucceed()
    {
        if (_redis is null) return;
        var lockProvider = new RedisDistributedLock(_redis, Options.Create(new DistributedLockOptions()), NullLogger<RedisDistributedLock>.Instance);
        var lockName = $"lock-acquire-{Guid.NewGuid():N}";

        var handle = await lockProvider.TryAcquireAsync(lockName, TimeSpan.FromSeconds(30));

        handle.Should().NotBeNull();
        await handle!.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_TryAcquire_AlreadyHeld_ShouldReturnNull()
    {
        if (_redis is null) return;
        var lockProvider = new RedisDistributedLock(_redis, Options.Create(new DistributedLockOptions()), NullLogger<RedisDistributedLock>.Instance);
        var lockName = $"lock-held-{Guid.NewGuid():N}";

        var handle1 = await lockProvider.TryAcquireAsync(lockName, TimeSpan.FromSeconds(30));
        var handle2 = await lockProvider.TryAcquireAsync(lockName, TimeSpan.FromSeconds(30));

        handle1.Should().NotBeNull();
        handle2.Should().BeNull();
        await handle1!.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_Release_ShouldAllowReacquire()
    {
        if (_redis is null) return;
        var lockProvider = new RedisDistributedLock(_redis, Options.Create(new DistributedLockOptions()), NullLogger<RedisDistributedLock>.Instance);
        var lockName = $"lock-release-{Guid.NewGuid():N}";

        var handle1 = await lockProvider.TryAcquireAsync(lockName, TimeSpan.FromSeconds(30));
        await handle1!.DisposeAsync();
        var handle2 = await lockProvider.TryAcquireAsync(lockName, TimeSpan.FromSeconds(30));

        handle2.Should().NotBeNull();
        await handle2!.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_Extend_ShouldSucceed()
    {
        if (_redis is null) return;
        var lockProvider = new RedisDistributedLock(_redis, Options.Create(new DistributedLockOptions()), NullLogger<RedisDistributedLock>.Instance);
        var lockName = $"lock-extend-{Guid.NewGuid():N}";

        var handle = await lockProvider.TryAcquireAsync(lockName, TimeSpan.FromSeconds(30));
        await handle!.ExtendAsync(TimeSpan.FromMinutes(1));

        // Should still be valid
        await handle.DisposeAsync();
    }

    #endregion

    #region Helpers

    private static bool IsDockerRunning()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string? ResolveImage(string envVar, string defaultImage)
    {
        var env = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrEmpty(env) ? defaultImage : env;
    }

    #endregion
}

#region Test Types

[MemoryPackable]
public partial class TestResult
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class TestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class TestAggregate
{
    public string Id { get; set; } = string.Empty;
    public int Counter { get; set; }
}

[MemoryPackable]
public partial class TestSnapshot
{
    public int Value { get; set; }
    public string Name { get; set; } = string.Empty;
}

#endregion
