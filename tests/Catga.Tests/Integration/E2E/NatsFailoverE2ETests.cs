using Catga.Abstractions;
using Catga.Core;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Inbox;
using Catga.Outbox;
using Catga.Persistence;
using Catga.Persistence.Nats;
using Catga.Persistence.Nats.Flow;
using Catga.Persistence.Nats.Stores;
using Catga.Persistence.Stores;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// TDD-style E2E tests for NATS failover, recovery, and edge case scenarios.
/// </summary>
[Trait("Requires", "Docker")]
public class NatsFailoverE2ETests : IAsyncLifetime
{
    private IContainer? _container;
    private NatsConnection? _nats;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        var image = ResolveImage("NATS_IMAGE", "nats:2.10-alpine");
        _container = new ContainerBuilder()
            .WithImage(image)
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8222).ForPath("/varz")))
            .Build();
        await _container.StartAsync();

        var port = _container.GetMappedPublicPort(4222);
        _nats = new NatsConnection(new NatsOpts { Url = $"nats://localhost:{port}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        await _nats.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        if (_nats is not null) await _nats.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    #region Outbox Failover Tests

    [Fact]
    public async Task Nats_Outbox_ProcessorCrash_PendingMessages_ShouldBeRecoverable()
    {
        if (_nats is null) return;

        var streamName = $"OUTBOX_CRASH_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(_nats, _serializer, _provider, streamName);
        var messageIds = new List<long>();

        // Add messages before crash
        for (int i = 0; i < 5; i++)
        {
            var msgId = MessageExtensions.NewMessageId();
            messageIds.Add(msgId);
            var msg = new OutboxMessage
            {
                MessageId = msgId,
                MessageType = "NatsFailoverTestMessage",
                Payload = _serializer.Serialize(new NatsFailoverTestMessage { Data = $"msg-{i}" }),
                CreatedAt = DateTime.UtcNow,
                Status = OutboxStatus.Pending
            };
            await outbox.AddAsync(msg);
        }
        await Task.Delay(200);

        // Simulate crash - create new outbox instance
        var recoveredOutbox = new NatsJSOutboxStore(_nats, _serializer, _provider, streamName);
        var pending = await recoveredOutbox.GetPendingMessagesAsync(100);

        pending.Should().HaveCount(5);
        foreach (var msgId in messageIds)
        {
            pending.Should().Contain(m => m.MessageId == msgId);
        }
    }

    [Fact]
    public async Task Nats_Outbox_PartialPublish_ShouldNotLoseMessages()
    {
        if (_nats is null) return;

        var streamName = $"OUTBOX_PARTIAL_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(_nats, _serializer, _provider, streamName);
        var messageIds = new List<long>();

        for (int i = 0; i < 5; i++)
        {
            var msgId = MessageExtensions.NewMessageId();
            messageIds.Add(msgId);
            var msg = new OutboxMessage
            {
                MessageId = msgId,
                MessageType = "NatsFailoverTestMessage",
                Payload = _serializer.Serialize(new NatsFailoverTestMessage { Data = $"msg-{i}" }),
                CreatedAt = DateTime.UtcNow,
                Status = OutboxStatus.Pending
            };
            await outbox.AddAsync(msg);
        }
        await Task.Delay(200);

        // Partial publish - only first 2
        await outbox.MarkAsPublishedAsync(messageIds[0]);
        await outbox.MarkAsPublishedAsync(messageIds[1]);
        await Task.Delay(100);

        var pending = await outbox.GetPendingMessagesAsync(100);

        pending.Should().HaveCount(3);
        pending.Should().NotContain(m => m.MessageId == messageIds[0]);
        pending.Should().NotContain(m => m.MessageId == messageIds[1]);
    }

    #endregion

    #region Inbox Failover Tests

    [Fact]
    public async Task Nats_Inbox_LockExpiry_ShouldAllowReprocessing()
    {
        if (_nats is null) return;

        var streamName = $"INBOX_LOCK_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();

        // First processor acquires lock with short duration
        var locked = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMilliseconds(100));
        locked.Should().BeTrue();

        // Wait for lock to expire
        await Task.Delay(200);

        // Second processor should be able to acquire
        var reacquired = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        reacquired.Should().BeTrue();
    }

    [Fact]
    public async Task Nats_Inbox_ConcurrentProcessing_OnlyOneSucceeds()
    {
        if (_nats is null) return;

        var streamName = $"INBOX_CONCURRENT_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();
        var successCount = 0;

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var locked = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
            if (locked)
            {
                Interlocked.Increment(ref successCount);
            }
        });

        await Task.WhenAll(tasks);

        successCount.Should().Be(1);
    }

    #endregion

    #region Flow Failover Tests

    [Fact]
    public async Task Nats_Flow_ProcessorCrash_OrphanedFlow_ShouldBeClaimed()
    {
        if (_nats is null) return;

        var bucket = $"flows_orphan_{Guid.NewGuid():N}";
        var flowStore = new NatsFlowStore(_nats, _serializer, bucket);
        var flowId = $"orphan-flow-{Guid.NewGuid():N}";

        // Create flow with old heartbeat
        var flow = new FlowState
        {
            Id = flowId,
            Type = "NatsFailoverTestFlow",
            Status = FlowStatus.Running,
            Step = 5,
            Version = 1,
            Owner = "crashed-processor",
            HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds()
        };
        await flowStore.CreateAsync(flow);

        // New processor tries to claim
        var claimed = await flowStore.TryClaimAsync("NatsFailoverTestFlow", "new-processor", timeoutMs: 60000);

        claimed.Should().NotBeNull();
        claimed!.Owner.Should().Be("new-processor");
        claimed.Step.Should().Be(5);
    }

    [Fact]
    public async Task Nats_Flow_HeartbeatMaintenance_ShouldPreventClaiming()
    {
        if (_nats is null) return;

        var bucket = $"flows_active_{Guid.NewGuid():N}";
        var flowStore = new NatsFlowStore(_nats, _serializer, bucket);
        var flowId = $"active-flow-{Guid.NewGuid():N}";

        // Create flow with recent heartbeat
        var flow = new FlowState
        {
            Id = flowId,
            Type = "NatsFailoverTestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 1,
            Owner = "active-processor",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await flowStore.CreateAsync(flow);

        // Another processor tries to claim
        var claimed = await flowStore.TryClaimAsync("NatsFailoverTestFlow", "intruder", timeoutMs: 60000);

        // Should not be claimed by intruder
        if (claimed is not null)
        {
            claimed.Owner.Should().Be("active-processor");
        }
    }

    [Fact]
    public async Task Nats_Flow_Heartbeat_ShouldUpdateTimestamp()
    {
        if (_nats is null) return;

        var bucket = $"flows_hb_{Guid.NewGuid():N}";
        var flowStore = new NatsFlowStore(_nats, _serializer, bucket);
        var flowId = $"hb-flow-{Guid.NewGuid():N}";

        var oldHeartbeat = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        var flow = new FlowState
        {
            Id = flowId,
            Type = "NatsFailoverTestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = oldHeartbeat
        };
        await flowStore.CreateAsync(flow);

        var result = await flowStore.HeartbeatAsync(flowId, "node-1", 0);

        result.Should().BeTrue();

        var updated = await flowStore.GetAsync(flowId);
        updated.Should().NotBeNull();
        updated!.HeartbeatAt.Should().BeGreaterThan(oldHeartbeat);
    }

    #endregion

    #region DslFlowStore Failover Tests

    [Fact]
    public async Task Nats_DslFlow_RecoveryAfterCrash_ShouldRestoreState()
    {
        if (_nats is null) return;

        var bucket = $"dslflows_crash_{Guid.NewGuid():N}";
        var dslFlowStore = new NatsDslFlowStore(_nats, _serializer, bucket);
        var flowId = $"dsl-crash-{Guid.NewGuid():N}";

        // Create flow with state
        var snapshot = new FlowSnapshot<NatsFailoverTestFlowState>(
            flowId,
            new NatsFailoverTestFlowState { Counter = 42 },
            CurrentStep: 3,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 5);
        await dslFlowStore.CreateAsync(snapshot);

        // Simulate crash - create new store instance
        var recoveredStore = new NatsDslFlowStore(_nats, _serializer, bucket);
        var recovered = await recoveredStore.GetAsync<NatsFailoverTestFlowState>(flowId);

        recovered.Should().NotBeNull();
        recovered!.State.Counter.Should().Be(42);
        recovered.CurrentStep.Should().Be(3);
        recovered.Version.Should().Be(5);
    }

    [Fact]
    public async Task Nats_DslFlow_ConcurrentUpdate_ShouldHandleVersioning()
    {
        if (_nats is null) return;

        var bucket = $"dslflows_concurrent_{Guid.NewGuid():N}";
        var dslFlowStore = new NatsDslFlowStore(_nats, _serializer, bucket);
        var flowId = $"dsl-concurrent-{Guid.NewGuid():N}";

        var snapshot = new FlowSnapshot<NatsFailoverTestFlowState>(
            flowId,
            new NatsFailoverTestFlowState { Counter = 0 },
            CurrentStep: 0,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);
        await dslFlowStore.CreateAsync(snapshot);

        var successCount = 0;

        // Concurrent updates
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var current = await dslFlowStore.GetAsync<NatsFailoverTestFlowState>(flowId);
            if (current is not null)
            {
                var updated = current with
                {
                    State = new NatsFailoverTestFlowState { Counter = i },
                    Version = current.Version + 1
                };
                var result = await dslFlowStore.UpdateAsync(updated);
                if (result) Interlocked.Increment(ref successCount);
            }
        });

        await Task.WhenAll(tasks);

        // At least one should succeed
        successCount.Should().BeGreaterOrEqualTo(1);
    }

    #endregion

    #region Idempotency Failover Tests

    [Fact]
    public async Task Nats_Idempotency_ConcurrentMarking_ShouldBeConsistent()
    {
        if (_nats is null) return;

        var streamName = $"IDEM_CONCURRENT_{Guid.NewGuid():N}";
        var idempotency = new NatsJSIdempotencyStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();

        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            if (!await idempotency.HasBeenProcessedAsync(messageId))
            {
                await idempotency.MarkAsProcessedAsync(messageId, new NatsFailoverTestResult { Value = i });
            }
        });

        await Task.WhenAll(tasks);
        await Task.Delay(200);

        var processed = await idempotency.HasBeenProcessedAsync(messageId);
        processed.Should().BeTrue();
    }

    [Fact]
    public async Task Nats_Idempotency_GetCachedResult_ShouldReturnValue()
    {
        if (_nats is null) return;

        var streamName = $"IDEM_CACHE_{Guid.NewGuid():N}";
        var idempotency = new NatsJSIdempotencyStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();

        await idempotency.MarkAsProcessedAsync(messageId, new NatsFailoverTestResult { Value = 99 });
        await Task.Delay(200);

        var result = await idempotency.GetCachedResultAsync<NatsFailoverTestResult>(messageId);

        result.Should().NotBeNull();
        result!.Value.Should().Be(99);
    }

    #endregion

    #region DLQ Failover Tests

    [Fact]
    public async Task Nats_DLQ_MultipleFailures_ShouldTrack()
    {
        if (_nats is null) return;

        var streamName = $"DLQ_MULTI_{Guid.NewGuid():N}";
        var dlq = new NatsJSDeadLetterQueue(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();
        var message = new NatsFailoverTestMessage { MessageId = messageId, Data = "dlq-test" };

        await dlq.SendAsync(message, new Exception("Failure 1"), retryCount: 1);
        await dlq.SendAsync(message, new Exception("Failure 2"), retryCount: 2);
        await dlq.SendAsync(message, new Exception("Failure 3"), retryCount: 3);
        await Task.Delay(200);

        var failed = await dlq.GetFailedMessagesAsync(100);

        failed.Should().NotBeEmpty();
        failed.Should().Contain(m => m.MessageId == messageId);
    }

    [Fact]
    public async Task Nats_DLQ_RecoveryAfterRestart_ShouldRetainMessages()
    {
        if (_nats is null) return;

        var streamName = $"DLQ_RECOVER_{Guid.NewGuid():N}";
        var dlq = new NatsJSDeadLetterQueue(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();

        await dlq.SendAsync(new NatsFailoverTestMessage { MessageId = messageId, Data = "persist" }, new Exception("Error"), retryCount: 1);
        await Task.Delay(200);

        // Simulate restart - new instance
        var recoveredDlq = new NatsJSDeadLetterQueue(_nats, _serializer, _provider, streamName);
        var failed = await recoveredDlq.GetFailedMessagesAsync(100);

        failed.Should().Contain(m => m.MessageId == messageId);
    }

    #endregion

    #region Snapshot Store Failover Tests

    [Fact]
    public async Task Nats_SnapshotStore_RecoveryAfterCrash_ShouldRetainSnapshot()
    {
        if (_nats is null) return;

        var snapshotStore = new NatsSnapshotStore(_nats, _serializer, Options.Create(new SnapshotOptions()), NullLogger<NatsSnapshotStore>.Instance);
        var streamId = $"stream-{Guid.NewGuid():N}";

        await snapshotStore.SaveAsync(streamId, new NatsFailoverTestAggregate { Id = streamId, Counter = 100 }, version: 10);

        // Simulate crash - new instance
        var recoveredStore = new NatsSnapshotStore(_nats, _serializer, Options.Create(new SnapshotOptions()), NullLogger<NatsSnapshotStore>.Instance);
        var snapshot = await recoveredStore.LoadAsync<NatsFailoverTestAggregate>(streamId);

        snapshot.Should().NotBeNull();
        snapshot!.Value.State.Counter.Should().Be(100);
        snapshot.Value.Version.Should().Be(10);
    }

    [Fact]
    public async Task Nats_SnapshotStore_OverwriteSnapshot_ShouldUpdateVersion()
    {
        if (_nats is null) return;

        var snapshotStore = new NatsSnapshotStore(_nats, _serializer, Options.Create(new SnapshotOptions()), NullLogger<NatsSnapshotStore>.Instance);
        var streamId = $"stream-{Guid.NewGuid():N}";

        await snapshotStore.SaveAsync(streamId, new NatsFailoverTestAggregate { Id = streamId, Counter = 1 }, version: 1);
        await snapshotStore.SaveAsync(streamId, new NatsFailoverTestAggregate { Id = streamId, Counter = 50 }, version: 50);

        var snapshot = await snapshotStore.LoadAsync<NatsFailoverTestAggregate>(streamId);

        snapshot.Should().NotBeNull();
        snapshot!.Value.Version.Should().Be(50);
        snapshot.Value.State.Counter.Should().Be(50);
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

#region NATS Failover Test Types

[MemoryPackable]
public partial class NatsFailoverTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class NatsFailoverTestResult
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class NatsFailoverTestFlowState : IFlowState
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

[MemoryPackable]
public partial class NatsFailoverTestAggregate
{
    public string Id { get; set; } = string.Empty;
    public int Counter { get; set; }
}

#endregion
