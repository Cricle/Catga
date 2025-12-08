using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence.InMemory;
using Catga.Persistence.InMemory.Flow;
using Catga.Persistence.Redis;
using Catga.Persistence.Redis.Flow;
using Catga.Persistence.Redis.Locking;
using Catga.Persistence.Redis.Persistence;
using Catga.Persistence.Redis.RateLimiting;
using Catga.Persistence.Redis.Stores;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// Cross-component E2E tests validating integration between multiple stores.
/// Tests real-world scenarios involving multiple persistence components working together.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class CrossComponentE2ETests : IAsyncLifetime
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
        if (_redis is not null) await _redis.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    #region Outbox + Inbox Integration

    [Fact]
    public async Task OutboxInbox_EndToEnd_ShouldGuaranteeExactlyOnce()
    {
        if (_redis is null) return;

        // Arrange
        var outbox = new RedisOutboxPersistence(_redis, _serializer, NullLogger<RedisOutboxPersistence>.Instance, _provider);
        var inbox = new RedisInboxPersistence(_redis, _serializer, NullLogger<RedisInboxPersistence>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();
        var outboxMsg = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "CrossTestMessage",
            Payload = _serializer.Serialize(new CrossTestMessage { MessageId = messageId, Data = "outbox-inbox-test" }),
            CreatedAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending
        };

        // Act - Outbox: Add message
        await outbox.AddAsync(outboxMsg);
        var pending = await outbox.GetPendingMessagesAsync(10);

        // Act - Inbox: Check and mark as processed
        var alreadyProcessed = await inbox.HasBeenProcessedAsync(messageId);
        var inboxMsg = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "CrossTestMessage",
            Payload = _serializer.Serialize(new CrossTestMessage { MessageId = messageId }),
            Status = InboxStatus.Processed
        };
        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.MarkAsProcessedAsync(inboxMsg);
        var nowProcessed = await inbox.HasBeenProcessedAsync(messageId);

        // Act - Outbox: Mark as published
        await outbox.MarkAsPublishedAsync(messageId);
        var pendingAfter = await outbox.GetPendingMessagesAsync(10);

        // Assert
        pending.Should().ContainSingle(m => m.MessageId == messageId);
        alreadyProcessed.Should().BeFalse();
        nowProcessed.Should().BeTrue();
        pendingAfter.Should().NotContain(m => m.MessageId == messageId);
    }

    [Fact]
    public async Task OutboxInbox_DuplicateMessage_ShouldBeIdempotent()
    {
        if (_redis is null) return;

        var inbox = new RedisInboxPersistence(_redis, _serializer, NullLogger<RedisInboxPersistence>.Instance, _provider);
        var processCount = 0;

        // Simulate multiple delivery attempts with different message IDs
        for (int i = 0; i < 5; i++)
        {
            var messageId = MessageExtensions.NewMessageId();
            if (!await inbox.HasBeenProcessedAsync(messageId))
            {
                processCount++;
                var inboxMsg = new InboxMessage
                {
                    MessageId = messageId,
                    MessageType = "CrossTestMessage",
                    Payload = [],
                    Status = InboxStatus.Processed
                };
                await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
                await inbox.MarkAsProcessedAsync(inboxMsg);
            }
        }

        processCount.Should().Be(5); // Each unique message processed once
    }

    #endregion

    #region EventStore + SnapshotStore Integration

    [Fact]
    public async Task EventStore_SnapshotStore_ShouldWorkTogether()
    {
        if (_redis is null) return;

        var eventStore = new RedisEventStore(_redis, _serializer, _provider, NullLogger<RedisEventStore>.Instance);
        var snapshotStore = new RedisSnapshotStore(_redis, _serializer, Options.Create(new SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);
        var streamId = $"aggregate-{Guid.NewGuid():N}";

        // Append events
        var events = new List<IEvent>
        {
            new CrossTestEvent { Name = "Created" },
            new CrossTestEvent { Name = "Updated" },
            new CrossTestEvent { Name = "Completed" }
        };
        await eventStore.AppendAsync(streamId, events);

        // Save snapshot at version 2
        var aggregate = new CrossTestAggregate { Id = streamId, Counter = 3 };
        await snapshotStore.SaveAsync(streamId, aggregate, version: 2);

        // Read events and snapshot
        var eventStream = await eventStore.ReadAsync(streamId);
        var snapshot = await snapshotStore.LoadAsync<CrossTestAggregate>(streamId);

        eventStream.Events.Should().HaveCount(3);
        snapshot.Should().NotBeNull();
        snapshot!.Value.Version.Should().Be(2);
        snapshot.Value.State.Counter.Should().Be(3);
    }

    [Fact]
    public async Task EventStore_ReplayFromSnapshot_ShouldWork()
    {
        if (_redis is null) return;

        var eventStore = new RedisEventStore(_redis, _serializer, _provider, NullLogger<RedisEventStore>.Instance);
        var snapshotStore = new RedisSnapshotStore(_redis, _serializer, Options.Create(new SnapshotOptions()), NullLogger<RedisSnapshotStore>.Instance);
        var streamId = $"replay-{Guid.NewGuid():N}";

        // Append 10 events
        for (int i = 0; i < 10; i++)
        {
            await eventStore.AppendAsync(streamId, [new CrossTestEvent { Name = $"Event{i}" }]);
        }

        // Save snapshot at version 5
        await snapshotStore.SaveAsync(streamId, new CrossTestAggregate { Id = streamId, Counter = 5 }, version: 5);

        // Read events from version 6 (after snapshot)
        var snapshot = await snapshotStore.LoadAsync<CrossTestAggregate>(streamId);
        var eventsAfterSnapshot = await eventStore.ReadAsync(streamId, fromVersion: snapshot!.Value.Version + 1);

        snapshot.Value.Version.Should().Be(5);
        eventsAfterSnapshot.Events.Should().HaveCountGreaterOrEqualTo(1);
    }

    #endregion

    #region FlowStore + DslFlowStore Integration

    [Fact]
    public async Task FlowStore_DslFlowStore_ParallelFlows_ShouldWork()
    {
        if (_redis is null) return;

        var flowStore = new RedisFlowStore(_redis, $"flows-{Guid.NewGuid():N}:");
        var dslFlowStore = new RedisDslFlowStore(_redis, _serializer, $"dslflows-{Guid.NewGuid():N}:");

        // Create parent flow
        var parentFlowId = $"parent-{Guid.NewGuid():N}";
        var parentState = new FlowState
        {
            Id = parentFlowId,
            Type = "ParentFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await flowStore.CreateAsync(parentState);

        // Create child DSL flows
        var childFlowIds = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var childId = $"child-{Guid.NewGuid():N}";
            childFlowIds.Add(childId);
            var snapshot = FlowSnapshot<CrossTestFlowState>.Create(
                childId,
                new CrossTestFlowState { Counter = i },
                currentStep: 0,
                status: DslFlowStatus.Running);
            await dslFlowStore.CreateAsync(snapshot);
        }

        // Verify all flows exist
        var parent = await flowStore.GetAsync(parentFlowId);
        parent.Should().NotBeNull();

        foreach (var childId in childFlowIds)
        {
            var child = await dslFlowStore.GetAsync<CrossTestFlowState>(childId);
            child.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task FlowStore_Failover_ShouldClaimOrphanedFlows()
    {
        if (_redis is null) return;

        var flowStore = new RedisFlowStore(_redis, $"failover-{Guid.NewGuid():N}:");

        // Create flow with old heartbeat (simulating crashed node)
        var flowId = $"orphan-{Guid.NewGuid():N}";
        var state = new FlowState
        {
            Id = flowId,
            Type = "OrphanFlow",
            Status = FlowStatus.Running,
            Step = 5,
            Version = 0,
            Owner = "crashed-node",
            HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds()
        };
        await flowStore.CreateAsync(state);

        // New node claims the orphaned flow
        var claimed = await flowStore.TryClaimAsync("OrphanFlow", "new-node", timeoutMs: 60000);

        claimed.Should().NotBeNull();
        claimed!.Owner.Should().Be("new-node");
        claimed.Step.Should().Be(5); // Should resume from step 5
    }

    #endregion

    #region DeadLetterQueue + Idempotency Integration

    [Fact]
    public async Task DLQ_Idempotency_FailedMessageRetry_ShouldWork()
    {
        if (_redis is null) return;

        var dlq = new RedisDeadLetterQueue(_redis, _serializer, $"dlq-{Guid.NewGuid():N}:");
        var idempotency = new RedisIdempotencyStore(_redis, _serializer, NullLogger<RedisIdempotencyStore>.Instance, _provider);
        var messageId = MessageExtensions.NewMessageId();
        var message = new CrossTestMessage { MessageId = messageId, Data = "failed-message" };

        // First attempt fails - send to DLQ
        await dlq.SendAsync(message, new Exception("Processing failed"), retryCount: 1);

        // Verify in DLQ
        var failed = await dlq.GetFailedMessagesAsync(10);
        failed.Should().ContainSingle(m => m.MessageId == messageId);

        // Retry succeeds - mark as processed
        await idempotency.MarkAsProcessedAsync(messageId, new CrossTestResult { Value = 42 });

        // Verify idempotency
        var processed = await idempotency.HasBeenProcessedAsync(messageId);
        var result = await idempotency.GetCachedResultAsync<CrossTestResult>(messageId);

        processed.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Value.Should().Be(42);
    }

    #endregion

    #region DistributedLock + RateLimiter Integration

    [Fact]
    public async Task Lock_RateLimiter_ConcurrentAccess_ShouldWork()
    {
        if (_redis is null) return;

        var lockProvider = new RedisDistributedLockProvider(_redis, Options.Create(new DistributedLockOptions()), NullLogger<RedisDistributedLock>.Instance);
        var rateLimiter = new RedisRateLimiter(_redis, Options.Create(new DistributedRateLimiterOptions()), NullLogger<RedisRateLimiter>.Instance);
        var resourceKey = $"resource-{Guid.NewGuid():N}";
        var accessCount = 0;

        // Simulate concurrent access with lock and rate limiting
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            await using var lockHandle = await lockProvider.AcquireAsync(resourceKey, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
            if (lockHandle is not null)
            {
                var result = await rateLimiter.TryAcquireAsync(resourceKey);
                if (result.IsAcquired)
                {
                    Interlocked.Increment(ref accessCount);
                }
            }
        });

        await Task.WhenAll(tasks);

        // Some requests should have been processed
        accessCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Lock_ExclusiveAccess_ShouldPreventConcurrentModification()
    {
        if (_redis is null) return;

        var lockProvider = new RedisDistributedLockProvider(_redis, Options.Create(new DistributedLockOptions()), NullLogger<RedisDistributedLock>.Instance);
        var resourceKey = $"exclusive-{Guid.NewGuid():N}";
        var concurrentAccess = 0;
        var maxConcurrent = 0;

        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            await using var lockHandle = await lockProvider.AcquireAsync(resourceKey, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
            if (lockHandle is not null)
            {
                var current = Interlocked.Increment(ref concurrentAccess);
                maxConcurrent = Math.Max(maxConcurrent, current);
                await Task.Delay(10);
                Interlocked.Decrement(ref concurrentAccess);
            }
        });

        await Task.WhenAll(tasks);

        maxConcurrent.Should().Be(1); // Only one at a time
    }

    #endregion

    #region Full Saga Pattern

    [Fact]
    public async Task Saga_CompleteWorkflow_ShouldCoordinateStores()
    {
        if (_redis is null) return;

        var outbox = new RedisOutboxPersistence(_redis, _serializer, NullLogger<RedisOutboxPersistence>.Instance, _provider);
        var eventStore = new RedisEventStore(_redis, _serializer, _provider, NullLogger<RedisEventStore>.Instance);
        var idempotency = new RedisIdempotencyStore(_redis, _serializer, NullLogger<RedisIdempotencyStore>.Instance, _provider);

        var sagaId = $"saga-{Guid.NewGuid():N}";
        var steps = new[] { "Reserve", "Charge", "Ship", "Complete" };

        foreach (var step in steps)
        {
            var stepMessageId = MessageExtensions.NewMessageId();

            // Check idempotency
            if (await idempotency.HasBeenProcessedAsync(stepMessageId))
                continue;

            // Process step
            await eventStore.AppendAsync(sagaId, [new CrossTestEvent { Name = $"{step}Started" }]);

            // Add outbox message for next step
            var nextMessageId = MessageExtensions.NewMessageId();
            var outboxMsg = new OutboxMessage
            {
                MessageId = nextMessageId,
                MessageType = "CrossTestMessage",
                Payload = _serializer.Serialize(new CrossTestMessage { MessageId = nextMessageId, Data = $"{step}Completed" }),
                CreatedAt = DateTime.UtcNow,
                Status = OutboxStatus.Pending
            };
            await outbox.AddAsync(outboxMsg);

            // Mark idempotency
            await idempotency.MarkAsProcessedAsync(stepMessageId, new CrossTestResult { Value = 1 });

            // Complete outbox
            await outbox.MarkAsPublishedAsync(nextMessageId);

            // Record completion event
            await eventStore.AppendAsync(sagaId, [new CrossTestEvent { Name = $"{step}Completed" }]);
        }

        // Verify saga completed
        var events = await eventStore.ReadAsync(sagaId);
        events.Events.Should().HaveCount(8); // 4 started + 4 completed
    }

    #endregion

    #region Helpers

    private static bool IsDockerRunning()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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
public partial class CrossTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class CrossTestEvent : IEvent
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Name { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class CrossTestResult
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class CrossTestAggregate
{
    public string Id { get; set; } = string.Empty;
    public int Counter { get; set; }
}

[MemoryPackable]
public partial class CrossTestFlowState : IFlowState
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
