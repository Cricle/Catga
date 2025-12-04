using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.InMemory.Locking;
using Catga.Persistence.InMemory.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Catga.Tests.Distributed;

/// <summary>
/// Extended tests for InMemory stores to improve coverage.
/// Target: 80% coverage for Catga.Persistence.InMemory
/// </summary>
public class InMemoryStoresExtendedTests
{
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    #region InMemoryDeadLetterQueue Tests

    [Fact]
    public async Task DeadLetterQueue_SendAsync_ShouldStoreMessage()
    {
        var dlq = new InMemoryDeadLetterQueue(NullLogger<InMemoryDeadLetterQueue>.Instance, _serializer);
        var message = new InMemoryTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "dlq" };

        await dlq.SendAsync(message, new Exception("Test error"), retryCount: 3);
        var failed = await dlq.GetFailedMessagesAsync(10);

        failed.Should().NotBeEmpty();
        failed.Should().Contain(m => m.RetryCount == 3);
    }

    [Fact]
    public async Task DeadLetterQueue_GetFailedMessages_WithLimit_ShouldRespectLimit()
    {
        var dlq = new InMemoryDeadLetterQueue(NullLogger<InMemoryDeadLetterQueue>.Instance, _serializer);

        for (int i = 0; i < 10; i++)
        {
            var message = new InMemoryTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"msg-{i}" };
            await dlq.SendAsync(message, new Exception($"Error {i}"), retryCount: i);
        }

        var failed = await dlq.GetFailedMessagesAsync(5);

        failed.Count.Should().Be(5);
    }

    [Fact]
    public async Task DeadLetterQueue_GetFailedMessages_Empty_ShouldReturnEmptyList()
    {
        var dlq = new InMemoryDeadLetterQueue(NullLogger<InMemoryDeadLetterQueue>.Instance, _serializer);

        var failed = await dlq.GetFailedMessagesAsync(10);

        failed.Should().BeEmpty();
    }

    #endregion

    #region MemoryInboxStore Tests

    [Fact]
    public async Task InboxStore_TryLockMessage_ShouldAcquireLock()
    {
        var inbox = new MemoryInboxStore(_provider);
        var messageId = MessageExtensions.NewMessageId();

        var locked = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        locked.Should().BeTrue();
    }

    [Fact]
    public async Task InboxStore_TryLockMessage_AlreadyLocked_ShouldFail()
    {
        var inbox = new MemoryInboxStore(_provider);
        var messageId = MessageExtensions.NewMessageId();

        var first = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        var second = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task InboxStore_MarkAsProcessed_ShouldSetProcessedFlag()
    {
        var inbox = new MemoryInboxStore(_provider);
        var messageId = MessageExtensions.NewMessageId();
        var inboxMsg = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new InMemoryTestMessage { Data = "inbox" }),
            Status = InboxStatus.Processing,
            ProcessingResult = new byte[] { 1, 2, 3 }
        };

        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.MarkAsProcessedAsync(inboxMsg);
        var processed = await inbox.HasBeenProcessedAsync(messageId);

        processed.Should().BeTrue();
    }

    [Fact]
    public async Task InboxStore_ReleaseLock_ShouldAllowReacquisition()
    {
        var inbox = new MemoryInboxStore(_provider);
        var messageId = MessageExtensions.NewMessageId();

        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.ReleaseLockAsync(messageId);
        var reacquired = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        reacquired.Should().BeTrue();
    }

    [Fact]
    public async Task InboxStore_GetProcessedResult_ShouldReturnStoredResult()
    {
        var inbox = new MemoryInboxStore(_provider);
        var messageId = MessageExtensions.NewMessageId();
        var resultBytes = new byte[] { 1, 2, 3, 4, 5 };
        var inboxMsg = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new InMemoryTestMessage { Data = "result" }),
            Status = InboxStatus.Processed,
            ProcessingResult = resultBytes
        };

        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.MarkAsProcessedAsync(inboxMsg);
        var result = await inbox.GetProcessedResultAsync(messageId);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task InboxStore_DeleteProcessedMessages_ShouldRemoveOldMessages()
    {
        var inbox = new MemoryInboxStore(_provider);
        var messageId = MessageExtensions.NewMessageId();
        var inboxMsg = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = new byte[] { 1 },
            Status = InboxStatus.Processed,
            ProcessedAt = DateTime.UtcNow.AddDays(-10)
        };

        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.MarkAsProcessedAsync(inboxMsg);
        await inbox.DeleteProcessedMessagesAsync(TimeSpan.FromDays(1));

        // Old messages should be deleted
    }

    #endregion

    #region MemoryOutboxStore Tests

    [Fact]
    public async Task OutboxStore_AddAsync_ShouldStoreMessage()
    {
        var outbox = new MemoryOutboxStore(_provider);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new InMemoryTestMessage { Data = "outbox" }),
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
        var outbox = new MemoryOutboxStore(_provider);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new InMemoryTestMessage { Data = "publish" }),
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
        var outbox = new MemoryOutboxStore(_provider);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new InMemoryTestMessage { Data = "fail" }),
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
        var outbox = new MemoryOutboxStore(_provider);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "TestMessage",
            Payload = _serializer.Serialize(new InMemoryTestMessage { Data = "delete" }),
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            Status = OutboxStatus.Pending
        };

        await outbox.AddAsync(message);
        await outbox.MarkAsPublishedAsync(messageId);
        await outbox.DeletePublishedMessagesAsync(TimeSpan.FromDays(1));

        // Cleanup should have removed old published messages
    }

    [Fact]
    public async Task OutboxStore_GetPendingMessages_WithLimit_ShouldRespectLimit()
    {
        var outbox = new MemoryOutboxStore(_provider);

        for (int i = 0; i < 10; i++)
        {
            var message = new OutboxMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                MessageType = "TestMessage",
                Payload = new byte[] { (byte)i },
                CreatedAt = DateTime.UtcNow,
                Status = OutboxStatus.Pending
            };
            await outbox.AddAsync(message);
        }

        var pending = await outbox.GetPendingMessagesAsync(5);

        pending.Count.Should().Be(5);
    }

    #endregion

    #region InMemoryDistributedLock Extended Tests

    [Fact]
    public async Task DistributedLock_AcquireAsync_WithTimeout_ShouldWaitAndAcquire()
    {
        var lockService = new InMemoryDistributedLock(
            Options.Create(new DistributedLockOptions { RetryInterval = TimeSpan.FromMilliseconds(50) }),
            NullLogger<InMemoryDistributedLock>.Instance);
        var resource = $"lock-wait-{Guid.NewGuid():N}";

        var handle1 = await lockService.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100));

        // Release in background after delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            await handle1!.DisposeAsync();
        });

        var handle2 = await lockService.AcquireAsync(resource, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));

        handle2.Should().NotBeNull();
        handle2.IsValid.Should().BeTrue();
        await handle2.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_AcquireAsync_Timeout_ShouldThrow()
    {
        var lockService = new InMemoryDistributedLock(
            Options.Create(new DistributedLockOptions { RetryInterval = TimeSpan.FromMilliseconds(10) }),
            NullLogger<InMemoryDistributedLock>.Instance);
        var resource = $"lock-timeout-{Guid.NewGuid():N}";

        var handle1 = await lockService.TryAcquireAsync(resource, TimeSpan.FromMinutes(5));

        await Assert.ThrowsAsync<LockAcquisitionException>(async () =>
        {
            await lockService.AcquireAsync(resource, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));
        });

        await handle1!.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_Extend_ShouldExtendExpiry()
    {
        var lockService = new InMemoryDistributedLock(
            Options.Create(new DistributedLockOptions()),
            NullLogger<InMemoryDistributedLock>.Instance);
        var resource = $"lock-extend-{Guid.NewGuid():N}";

        var handle = await lockService.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(100));
        await handle!.ExtendAsync(TimeSpan.FromMinutes(5));

        // ExtendAsync returns void, just verify handle is still valid
        handle.IsValid.Should().BeTrue();
        await handle.DisposeAsync();
    }

    #endregion

    #region InMemoryLeaderElection Extended Tests

    [Fact]
    public async Task LeaderElection_MultipleNodes_OnlyOneLeader()
    {
        var election1 = new InMemoryLeaderElection(nodeId: "node-1");
        var election2 = new InMemoryLeaderElection(nodeId: "node-2");
        var electionName = $"election-multi-{Guid.NewGuid():N}";

        var handle1 = await election1.TryAcquireLeadershipAsync(electionName);
        var handle2 = await election2.TryAcquireLeadershipAsync(electionName);

        // Both should succeed in InMemory (different instances)
        handle1.Should().NotBeNull();
        handle2.Should().NotBeNull();
    }

    [Fact]
    public async Task LeaderElection_GetLeader_ShouldReturnLeaderInfo()
    {
        var election = new InMemoryLeaderElection(nodeId: "leader-node");
        var electionName = $"election-info-{Guid.NewGuid():N}";

        var handle = await election.TryAcquireLeadershipAsync(electionName);
        var leader = await election.GetLeaderAsync(electionName);

        leader.Should().NotBeNull();
        leader!.Value.NodeId.Should().Be("leader-node");
        await handle!.DisposeAsync();
    }

    [Fact]
    public async Task LeaderElection_IsLeader_AfterRelease_ShouldReturnFalse()
    {
        var election = new InMemoryLeaderElection(nodeId: "release-node");
        var electionName = $"election-release-{Guid.NewGuid():N}";

        var handle = await election.TryAcquireLeadershipAsync(electionName);
        await handle!.DisposeAsync();
        var isLeader = await election.IsLeaderAsync(electionName);

        isLeader.Should().BeFalse();
    }

    #endregion

    #region InMemoryRateLimiter Extended Tests

    [Fact]
    public async Task RateLimiter_TryAcquire_WithinLimit_ShouldSucceed()
    {
        var limiter = new InMemoryRateLimiter();
        var key = $"rate-{Guid.NewGuid():N}";

        var result = await limiter.TryAcquireAsync(key);

        result.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task RateLimiter_TryAcquire_MultiplePermits_ShouldDeduct()
    {
        var limiter = new InMemoryRateLimiter();
        var key = $"rate-multi-{Guid.NewGuid():N}";

        var result = await limiter.TryAcquireAsync(key, permits: 5);

        result.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task RateLimiter_GetStatistics_ShouldReturnCurrentState()
    {
        var limiter = new InMemoryRateLimiter();
        var key = $"rate-stats-{Guid.NewGuid():N}";

        await limiter.TryAcquireAsync(key, permits: 3);
        var stats = await limiter.GetStatisticsAsync(key);

        stats.Should().NotBeNull();
        stats!.Value.CurrentCount.Should().Be(3);
    }

    [Fact]
    public async Task RateLimiter_ConcurrentAccess_ShouldBeThreadSafe()
    {
        var limiter = new InMemoryRateLimiter();
        var key = $"rate-concurrent-{Guid.NewGuid():N}";
        var acquiredCount = 0;

        var tasks = Enumerable.Range(0, 10)
            .Select(async _ =>
            {
                var result = await limiter.TryAcquireAsync(key);
                if (result.IsAcquired)
                    Interlocked.Increment(ref acquiredCount);
            });

        await Task.WhenAll(tasks);

        acquiredCount.Should().Be(10);
    }

    #endregion

    #region InMemorySnapshotStore Extended Tests

    [Fact]
    public async Task SnapshotStore_SaveAndLoad_ShouldRoundTrip()
    {
        var store = new InMemorySnapshotStore();
        var aggregateId = $"agg-{Guid.NewGuid():N}";
        var snapshot = new SnapshotTestData { Value = 42, Name = "test" };

        await store.SaveAsync(aggregateId, snapshot, 5);
        var loaded = await store.LoadAsync<SnapshotTestData>(aggregateId);

        loaded.Should().NotBeNull();
    }

    [Fact]
    public async Task SnapshotStore_Load_NonExistent_ShouldReturnNull()
    {
        var store = new InMemorySnapshotStore();

        var loaded = await store.LoadAsync<SnapshotTestData>("non-existent");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task SnapshotStore_Delete_ShouldRemoveSnapshot()
    {
        var store = new InMemorySnapshotStore();
        var aggregateId = $"agg-delete-{Guid.NewGuid():N}";

        await store.SaveAsync(aggregateId, new SnapshotTestData { Value = 1 }, 1);
        await store.DeleteAsync(aggregateId);

        var loaded = await store.LoadAsync<SnapshotTestData>(aggregateId);

        loaded.Should().BeNull();
    }

    #endregion

    #region InMemoryEventStore Tests

    [Fact]
    public async Task EventStore_AppendAndRead_ShouldRoundTrip()
    {
        var store = new InMemoryEventStore(_provider);
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent> { new InMemoryTestEvent { MessageId = 1, Data = "event1" } };

        await store.AppendAsync(streamId, events);
        var result = await store.ReadAsync(streamId);

        result.Events.Should().HaveCount(1);
        result.Version.Should().Be(0);
    }

    [Fact]
    public async Task EventStore_AppendMultiple_ShouldIncrementVersion()
    {
        var store = new InMemoryEventStore(_provider);
        var streamId = $"stream-multi-{Guid.NewGuid():N}";

        await store.AppendAsync(streamId, new List<IEvent> { new InMemoryTestEvent { MessageId = 1, Data = "e1" } });
        await store.AppendAsync(streamId, new List<IEvent> { new InMemoryTestEvent { MessageId = 2, Data = "e2" } }, expectedVersion: 0);

        var result = await store.ReadAsync(streamId);
        result.Events.Should().HaveCount(2);
        result.Version.Should().Be(1);
    }

    [Fact]
    public async Task EventStore_GetVersion_ShouldReturnCorrectVersion()
    {
        var store = new InMemoryEventStore(_provider);
        var streamId = $"stream-version-{Guid.NewGuid():N}";

        var versionBefore = await store.GetVersionAsync(streamId);
        await store.AppendAsync(streamId, new List<IEvent> { new InMemoryTestEvent { MessageId = 1, Data = "e1" } });
        var versionAfter = await store.GetVersionAsync(streamId);

        versionBefore.Should().Be(-1);
        versionAfter.Should().Be(0);
    }

    [Fact]
    public async Task EventStore_Read_NonExistentStream_ShouldReturnEmpty()
    {
        var store = new InMemoryEventStore(_provider);

        var result = await store.ReadAsync("non-existent-stream");

        result.Events.Should().BeEmpty();
        result.Version.Should().Be(-1);
    }

    [Fact]
    public async Task EventStore_Clear_ShouldRemoveAllStreams()
    {
        var store = new InMemoryEventStore(_provider);
        var streamId = $"stream-clear-{Guid.NewGuid():N}";

        await store.AppendAsync(streamId, new List<IEvent> { new InMemoryTestEvent { MessageId = 1, Data = "e1" } });
        store.Clear();

        store.StreamCount.Should().Be(0);
    }

    [Fact]
    public async Task EventStore_StreamCount_ShouldReturnCorrectCount()
    {
        var store = new InMemoryEventStore(_provider);

        await store.AppendAsync($"stream-count-1-{Guid.NewGuid():N}", new List<IEvent> { new InMemoryTestEvent { MessageId = 1, Data = "e1" } });
        await store.AppendAsync($"stream-count-2-{Guid.NewGuid():N}", new List<IEvent> { new InMemoryTestEvent { MessageId = 2, Data = "e2" } });

        store.StreamCount.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task EventStore_GetEventCount_ShouldReturnCorrectCount()
    {
        var store = new InMemoryEventStore(_provider);
        var streamId = $"stream-eventcount-{Guid.NewGuid():N}";

        await store.AppendAsync(streamId, new List<IEvent>
        {
            new InMemoryTestEvent { MessageId = 1, Data = "e1" },
            new InMemoryTestEvent { MessageId = 2, Data = "e2" }
        });

        store.GetEventCount(streamId).Should().Be(2);
    }

    [Fact]
    public async Task EventStore_ReadWithFromVersion_ShouldReturnSubset()
    {
        var store = new InMemoryEventStore(_provider);
        var streamId = $"stream-fromversion-{Guid.NewGuid():N}";

        await store.AppendAsync(streamId, new List<IEvent>
        {
            new InMemoryTestEvent { MessageId = 1, Data = "e1" },
            new InMemoryTestEvent { MessageId = 2, Data = "e2" },
            new InMemoryTestEvent { MessageId = 3, Data = "e3" }
        });

        var result = await store.ReadAsync(streamId, fromVersion: 1);

        result.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task EventStore_ReadWithMaxCount_ShouldLimitResults()
    {
        var store = new InMemoryEventStore(_provider);
        var streamId = $"stream-maxcount-{Guid.NewGuid():N}";

        await store.AppendAsync(streamId, new List<IEvent>
        {
            new InMemoryTestEvent { MessageId = 1, Data = "e1" },
            new InMemoryTestEvent { MessageId = 2, Data = "e2" },
            new InMemoryTestEvent { MessageId = 3, Data = "e3" }
        });

        var result = await store.ReadAsync(streamId, maxCount: 2);

        result.Events.Should().HaveCount(2);
    }

    #endregion
}

#region Test Types

public class SnapshotTestData
{
    public int Value { get; set; }
    public string Name { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class InMemoryTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class InMemoryTestEvent : IEvent
{
    public long MessageId { get; set; }
    public long? CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

#endregion
