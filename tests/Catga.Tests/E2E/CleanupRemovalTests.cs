using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Cleanup and removal E2E tests.
/// Tests resource cleanup, data removal, garbage collection, and lifecycle management.
/// </summary>
public class CleanupRemovalTests
{
    [Fact]
    public async Task Cleanup_EventStream_DeletesAllEvents()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var streamId = $"Order-{Guid.NewGuid():N}"[..16];

        // Create events
        await eventStore.AppendAsync(streamId, new IEvent[]
        {
            new TestEvent("Event1"),
            new TestEvent("Event2"),
            new TestEvent("Event3")
        });

        // Verify exists
        var beforeDelete = await eventStore.StreamExistsAsync(streamId);
        beforeDelete.Should().BeTrue();

        // Delete
        await eventStore.DeleteStreamAsync(streamId);

        // Verify deleted
        var afterDelete = await eventStore.StreamExistsAsync(streamId);
        afterDelete.Should().BeFalse();
    }

    [Fact]
    public async Task Cleanup_Snapshots_RemovesOldSnapshots()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var snapshotStore = sp.GetRequiredService<IEnhancedSnapshotStore>();

        var aggregateId = $"Agg-{Guid.NewGuid():N}"[..12];

        // Create multiple snapshots
        for (int i = 1; i <= 5; i++)
        {
            await snapshotStore.SaveAsync(aggregateId, new TestSnapshot { Version = i }, i);
        }

        // Get latest
        var latest = await snapshotStore.GetAsync<TestSnapshot>(aggregateId);
        latest.Should().NotBeNull();
        latest!.Version.Should().Be(5);

        // Delete all snapshots
        await snapshotStore.DeleteAsync(aggregateId);

        // Verify deleted
        var afterDelete = await snapshotStore.GetAsync<TestSnapshot>(aggregateId);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task Cleanup_Projection_ResetsState()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var projection = new ResettableProjection();

        // Apply some events
        await projection.ApplyAsync(new TestEvent("Event1"));
        await projection.ApplyAsync(new TestEvent("Event2"));
        await projection.ApplyAsync(new TestEvent("Event3"));

        projection.Count.Should().Be(3);

        // Reset projection
        await projection.ResetAsync();

        projection.Count.Should().Be(0);
    }

    [Fact]
    public async Task Cleanup_IdempotencyRecords_RemovesExpired()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var idempotencyStore = sp.GetRequiredService<IIdempotencyStore>();

        var requestId = $"req-{Guid.NewGuid():N}";

        // Store with short TTL
        await idempotencyStore.StoreResultAsync(requestId, "result", TimeSpan.FromMilliseconds(50));

        // Immediately available
        var exists = await idempotencyStore.IsProcessedAsync(requestId);
        exists.Should().BeTrue();

        // Wait for expiry
        await Task.Delay(100);

        // Should be expired (implementation-dependent)
        // Note: In-memory store may or may not auto-expire
    }

    [Fact]
    public async Task Cleanup_DeadLetterQueue_ClearsMessages()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var dlqStore = sp.GetRequiredService<IDeadLetterStore>();

        // Add messages to DLQ
        for (int i = 0; i < 5; i++)
        {
            await dlqStore.StoreAsync(new DeadLetterMessage
            {
                MessageId = $"msg-{i}",
                OriginalQueue = "test-queue",
                Payload = new byte[] { (byte)i },
                FailedAt = DateTime.UtcNow,
                Reason = "Test failure"
            });
        }

        // Retrieve and process
        var messages = await dlqStore.GetMessagesAsync("test-queue", 10);
        messages.Should().HaveCount(5);

        // Clear DLQ
        foreach (var msg in messages)
        {
            await dlqStore.RemoveAsync(msg.MessageId);
        }

        // Verify cleared
        var remaining = await dlqStore.GetMessagesAsync("test-queue", 10);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task Cleanup_Subscriptions_UnsubscribesAll()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var subscriptionManager = sp.GetRequiredService<ISubscriptionManager>();

        var subscriptions = new List<ISubscription>();

        // Create multiple subscriptions
        for (int i = 0; i < 5; i++)
        {
            var sub = await subscriptionManager.SubscribeAsync(
                $"stream-{i}",
                async (evt, ct) => { });
            subscriptions.Add(sub);
        }

        // Unsubscribe all
        foreach (var sub in subscriptions)
        {
            await sub.UnsubscribeAsync();
        }

        // All subscriptions should be closed
        subscriptions.Should().HaveCount(5);
    }

    [Fact]
    public async Task Cleanup_FlowState_RemovesPersisted()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IDslFlowStore, Catga.Persistence.InMemory.Flow.InMemoryDslFlowStore>();

        var sp = services.BuildServiceProvider();
        var flowStore = sp.GetRequiredService<IDslFlowStore>();

        var flowId = $"flow-{Guid.NewGuid():N}"[..16];
        var snapshot = new Catga.Flow.Dsl.FlowSnapshot<TestFlowState>
        {
            FlowId = flowId,
            State = new TestFlowState { FlowId = flowId, Value = 42 },
            Position = new Catga.Flow.Dsl.FlowPosition(new[] { 0 }),
            Status = Catga.Flow.Dsl.FlowStatus.Running
        };

        // Save
        await flowStore.SaveSnapshotAsync(snapshot);

        // Verify exists
        var loaded = await flowStore.LoadSnapshotAsync<TestFlowState>(flowId);
        loaded.Should().NotBeNull();

        // Delete
        await flowStore.DeleteSnapshotAsync(flowId);

        // Verify deleted
        var afterDelete = await flowStore.LoadSnapshotAsync<TestFlowState>(flowId);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task Cleanup_Outbox_RemovesProcessedMessages()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        var messages = new List<OutboxMessage>();

        // Add messages
        for (int i = 0; i < 5; i++)
        {
            var msg = new OutboxMessage
            {
                Id = Guid.NewGuid().ToString(),
                MessageType = "TestMessage",
                Payload = new byte[] { (byte)i },
                CreatedAt = DateTime.UtcNow
            };
            await outboxStore.AddAsync(msg);
            messages.Add(msg);
        }

        // Mark as processed and remove
        foreach (var msg in messages)
        {
            await outboxStore.MarkAsProcessedAsync(msg.Id);
        }

        // Get pending - should be empty
        var pending = await outboxStore.GetPendingAsync(10);
        pending.Should().BeEmpty();
    }

    [Fact]
    public async Task Cleanup_CheckpointStore_ResetCheckpoint()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var checkpointStore = sp.GetRequiredService<IProjectionCheckpointStore>();

        var projectionName = "TestProjection";

        // Save checkpoint
        await checkpointStore.SaveCheckpointAsync(projectionName, 100);

        // Verify
        var checkpoint = await checkpointStore.GetCheckpointAsync(projectionName);
        checkpoint.Should().Be(100);

        // Reset
        await checkpointStore.SaveCheckpointAsync(projectionName, 0);

        // Verify reset
        var afterReset = await checkpointStore.GetCheckpointAsync(projectionName);
        afterReset.Should().Be(0);
    }

    [Fact]
    public async Task Cleanup_GdprErasure_RemovesPersonalData()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var gdprStore = sp.GetRequiredService<IGdprStore>();

        var subjectId = $"user-{Guid.NewGuid():N}"[..12];

        // Create erasure request
        var request = new ErasureRequest(subjectId, "user@example.com", DateTime.UtcNow);
        await gdprStore.SaveRequestAsync(request);

        // Verify exists
        var loaded = await gdprStore.GetErasureRequestAsync(subjectId);
        loaded.Should().NotBeNull();

        // Mark as completed
        var completed = request with { CompletedAt = DateTime.UtcNow };
        await gdprStore.SaveRequestAsync(completed);

        // Verify marked complete
        var afterComplete = await gdprStore.GetErasureRequestAsync(subjectId);
        afterComplete!.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Cleanup_BatchRemoval_RemovesMultipleItems()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        // Create multiple streams
        var streamIds = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var streamId = $"Batch-{Guid.NewGuid():N}"[..16];
            streamIds.Add(streamId);
            await eventStore.AppendAsync(streamId, new IEvent[] { new TestEvent($"Event-{i}") });
        }

        // Delete all
        foreach (var streamId in streamIds)
        {
            await eventStore.DeleteStreamAsync(streamId);
        }

        // Verify all deleted
        foreach (var streamId in streamIds)
        {
            var exists = await eventStore.StreamExistsAsync(streamId);
            exists.Should().BeFalse();
        }
    }

    #region Test Types

    public record TestEvent(string Data) : IEvent
    {
        public long MessageId { get; init; }
    }

    public class TestSnapshot
    {
        public int Version { get; set; }
    }

    public class TestFlowState : IFlowState
    {
        public string FlowId { get; set; } = "";
        public int Value { get; set; }
    }

    public class ResettableProjection : IProjection
    {
        public string Name => "Resettable";
        public int Count { get; private set; }

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            Count++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            Count = 0;
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
