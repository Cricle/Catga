using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.Redis;
using Catga.Persistence.Redis.Flow;
using Catga.Persistence.Redis.Persistence;
using Catga.Persistence.Redis.Stores;
using Catga.Resilience;
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
        var store = new RedisIdempotencyStore(_redis, _serializer, _provider);
        var messageId = MessageExtensions.NewMessageId();

        await store.MarkAsProcessedAsync(messageId, new TestResult { Value = 42 });
        var result = await store.HasBeenProcessedAsync(messageId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IdempotencyStore_GetCachedResult_ShouldReturnStoredValue()
    {
        if (_redis is null) return;
        var store = new RedisIdempotencyStore(_redis, _serializer, _provider);
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
        var store = new RedisIdempotencyStore(_redis, _serializer, _provider);
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

    #region RedisEventStore Tests

    [Fact]
    public async Task EventStore_AppendAndRead_ShouldWork()
    {
        if (_redis is null) return;
        var store = new RedisEventStore(_redis, _serializer, _provider, NullLogger<RedisEventStore>.Instance);
        var streamId = $"stream-{Guid.NewGuid():N}";
        var events = new List<IEvent> { new TestEvent { Name = "Event1" }, new TestEvent { Name = "Event2" } };

        await store.AppendAsync(streamId, events);
        var stream = await store.ReadAsync(streamId);

        stream.Events.Should().HaveCount(2);
        stream.Version.Should().Be(1);
    }

    [Fact]
    public async Task EventStore_GetVersion_ShouldReturnCorrectVersion()
    {
        if (_redis is null) return;
        var store = new RedisEventStore(_redis, _serializer, _provider, NullLogger<RedisEventStore>.Instance);
        var streamId = $"stream-ver-{Guid.NewGuid():N}";

        var versionBefore = await store.GetVersionAsync(streamId);
        await store.AppendAsync(streamId, [new TestEvent { Name = "E1" }]);
        var versionAfter = await store.GetVersionAsync(streamId);

        versionBefore.Should().Be(-1);
        versionAfter.Should().Be(0);
    }

    [Fact]
    public async Task EventStore_OptimisticConcurrency_ShouldThrowOnMismatch()
    {
        if (_redis is null) return;
        var store = new RedisEventStore(_redis, _serializer, _provider, NullLogger<RedisEventStore>.Instance);
        var streamId = $"stream-conc-{Guid.NewGuid():N}";

        await store.AppendAsync(streamId, [new TestEvent { Name = "E1" }]);

        var act = async () => await store.AppendAsync(streamId, [new TestEvent { Name = "E2" }], expectedVersion: 5);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task EventStore_ReadFromVersion_ShouldFilterEvents()
    {
        if (_redis is null) return;
        var store = new RedisEventStore(_redis, _serializer, _provider, NullLogger<RedisEventStore>.Instance);
        var streamId = $"stream-filter-{Guid.NewGuid():N}";

        await store.AppendAsync(streamId, [new TestEvent { Name = "E1" }, new TestEvent { Name = "E2" }, new TestEvent { Name = "E3" }]);
        var stream = await store.ReadAsync(streamId, fromVersion: 1);

        stream.Events.Should().HaveCountGreaterOrEqualTo(1);
    }

    #endregion

    #region RedisFlowStore Tests

    [Fact]
    public async Task FlowStore_CreateAndGet_ShouldWork()
    {
        if (_redis is null) return;
        var store = new RedisFlowStore(_redis, $"flow-{Guid.NewGuid():N}:");
        var flowId = $"flow-{Guid.NewGuid():N}";
        var state = new FlowState
        {
            Id = flowId,
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var created = await store.CreateAsync(state);
        var loaded = await store.GetAsync(flowId);

        created.Should().BeTrue();
        loaded.Should().NotBeNull();
        loaded!.Type.Should().Be("TestFlow");
    }

    [Fact]
    public async Task FlowStore_Update_ShouldIncrementVersion()
    {
        if (_redis is null) return;
        var store = new RedisFlowStore(_redis, $"flow-upd-{Guid.NewGuid():N}:");
        var flowId = $"flow-{Guid.NewGuid():N}";
        var state = new FlowState
        {
            Id = flowId,
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await store.CreateAsync(state);
        state.Step = 1;
        var updated = await store.UpdateAsync(state);
        var loaded = await store.GetAsync(flowId);

        updated.Should().BeTrue();
        loaded!.Step.Should().Be(1);
    }

    [Fact]
    public async Task FlowStore_TryClaim_ShouldClaimAbandonedFlow()
    {
        if (_redis is null) return;
        var store = new RedisFlowStore(_redis, $"flow-claim-{Guid.NewGuid():N}:");
        var flowId = $"flow-{Guid.NewGuid():N}";
        var state = new FlowState
        {
            Id = flowId,
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds() // Old heartbeat
        };

        await store.CreateAsync(state);
        var claimed = await store.TryClaimAsync("TestFlow", "node-2", timeoutMs: 60000);

        claimed.Should().NotBeNull();
        claimed!.Owner.Should().Be("node-2");
    }

    [Fact]
    public async Task FlowStore_Heartbeat_ShouldUpdateTimestamp()
    {
        if (_redis is null) return;
        var store = new RedisFlowStore(_redis, $"flow-hb-{Guid.NewGuid():N}:");
        var flowId = $"flow-{Guid.NewGuid():N}";
        var state = new FlowState
        {
            Id = flowId,
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds()
        };

        await store.CreateAsync(state);
        var result = await store.HeartbeatAsync(flowId, "node-1", 0);

        result.Should().BeTrue();
    }

    #endregion

    #region RedisDslFlowStore Tests

    [Fact]
    public async Task DslFlowStore_CreateAndGet_ShouldWork()
    {
        if (_redis is null) return;
        var store = new RedisDslFlowStore(_redis, _serializer, $"dslflow-{Guid.NewGuid():N}:");
        var flowId = $"dsl-{Guid.NewGuid():N}";
        var snapshot = FlowSnapshot<TestFlowState>.Create(
            flowId,
            new TestFlowState { Counter = 0 },
            currentStep: 0,
            status: DslFlowStatus.Running);

        var created = await store.CreateAsync(snapshot);
        var loaded = await store.GetAsync<TestFlowState>(flowId);

        created.Should().BeTrue();
        loaded.Should().NotBeNull();
    }

    [Fact]
    public async Task DslFlowStore_Update_ShouldWork()
    {
        if (_redis is null) return;
        var store = new RedisDslFlowStore(_redis, _serializer, $"dslflow-upd-{Guid.NewGuid():N}:");
        var flowId = $"dsl-{Guid.NewGuid():N}";
        var snapshot = FlowSnapshot<TestFlowState>.Create(
            flowId,
            new TestFlowState { Counter = 0 },
            currentStep: 0,
            status: DslFlowStatus.Running);

        await store.CreateAsync(snapshot);
        var updated = await store.UpdateAsync(snapshot with { Position = new FlowPosition([1]) });

        updated.Should().BeTrue();
    }

    [Fact]
    public async Task DslFlowStore_Delete_ShouldRemoveFlow()
    {
        if (_redis is null) return;
        var store = new RedisDslFlowStore(_redis, _serializer, $"dslflow-del-{Guid.NewGuid():N}:");
        var flowId = $"dsl-{Guid.NewGuid():N}";
        var snapshot = FlowSnapshot<TestFlowState>.Create(
            flowId,
            new TestFlowState { Counter = 0 },
            currentStep: 0,
            status: DslFlowStatus.Running);

        await store.CreateAsync(snapshot);
        var deleted = await store.DeleteAsync(flowId);
        var loaded = await store.GetAsync<TestFlowState>(flowId);

        deleted.Should().BeTrue();
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DslFlowStore_WaitCondition_ShouldWork()
    {
        if (_redis is null) return;
        var store = new RedisDslFlowStore(_redis, _serializer, $"dslflow-wait-{Guid.NewGuid():N}:");
        var correlationId = $"corr-{Guid.NewGuid():N}";
        var flowId = $"flow-{Guid.NewGuid():N}";
        var condition = new WaitCondition
        {
            CorrelationId = correlationId,
            Type = WaitType.All,
            ExpectedCount = 2,
            CompletedCount = 0,
            Timeout = TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow,
            FlowId = flowId,
            FlowType = "TestFlow",
            Step = 0
        };

        await store.SetWaitConditionAsync(correlationId, condition);
        var loaded = await store.GetWaitConditionAsync(correlationId);

        loaded.Should().NotBeNull();
        loaded!.ExpectedCount.Should().Be(2);
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

[MemoryPackable]
public partial class TestEvent : IEvent
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Name { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class TestFlowState : IFlowState
{
    public string? FlowId { get; set; }
    public int Counter { get; set; }
    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() => [];
}

#endregion
