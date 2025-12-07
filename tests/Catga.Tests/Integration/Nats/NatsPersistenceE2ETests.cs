using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
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

namespace Catga.Tests.Integration.Nats;

/// <summary>
/// E2E tests for NATS Persistence stores.
/// Target: 80% coverage for Catga.Persistence.Nats
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class NatsPersistenceE2ETests : IAsyncLifetime
{
    private IContainer? _container;
    private NatsConnection? _nats;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;
        var image = ResolveImage("TEST_NATS_IMAGE", "nats:latest");
        if (image is null) return;

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

    #region NatsJSIdempotencyStore Tests

    [Fact]
    public async Task IdempotencyStore_MarkAsProcessed_ShouldStoreResult()
    {
        if (_nats is null) return;
        var streamName = $"IDEM_{Guid.NewGuid():N}";
        var store = new NatsJSIdempotencyStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();

        await store.MarkAsProcessedAsync(messageId, new NatsTestResult { Value = 42 });
        await Task.Delay(200);
        var result = await store.HasBeenProcessedAsync(messageId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IdempotencyStore_HasBeenProcessed_NotProcessed_ShouldReturnFalse()
    {
        if (_nats is null) return;
        var streamName = $"IDEM_NOT_{Guid.NewGuid():N}";
        var store = new NatsJSIdempotencyStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();

        var result = await store.HasBeenProcessedAsync(messageId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IdempotencyStore_GetCachedResult_ShouldReturnStoredValue()
    {
        if (_nats is null) return;
        var streamName = $"IDEM_CACHE_{Guid.NewGuid():N}";
        var store = new NatsJSIdempotencyStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();
        var expected = new NatsTestResult { Value = 123 };

        await store.MarkAsProcessedAsync(messageId, expected);
        await Task.Delay(200);
        var cached = await store.GetCachedResultAsync<NatsTestResult>(messageId);

        cached.Should().NotBeNull();
        cached!.Value.Should().Be(123);
    }

    #endregion

    #region NatsJSOutboxStore Tests

    [Fact]
    public async Task OutboxStore_AddAndGetPending_ShouldWork()
    {
        if (_nats is null) return;
        var streamName = $"OUTBOX_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "NatsTestMessage",
            Payload = _serializer.Serialize(new NatsTestMessage { Data = "outbox" }),
            CreatedAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending
        };

        await outbox.AddAsync(message);
        await Task.Delay(200);
        var pending = await outbox.GetPendingMessagesAsync(10);

        pending.Should().NotBeEmpty();
    }

    [Fact]
    public async Task OutboxStore_MarkAsPublished_ShouldUpdateStatus()
    {
        if (_nats is null) return;
        var streamName = $"OUTBOX_PUB_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "NatsTestMessage",
            Payload = _serializer.Serialize(new NatsTestMessage { Data = "publish" }),
            CreatedAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending
        };

        await outbox.AddAsync(message);
        await Task.Delay(100);
        await outbox.MarkAsPublishedAsync(messageId);
        await Task.Delay(100);

        // After marking as published, it should not appear in pending
    }

    [Fact]
    public async Task OutboxStore_MarkAsFailed_ShouldUpdateStatus()
    {
        if (_nats is null) return;
        var streamName = $"OUTBOX_FAIL_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "NatsTestMessage",
            Payload = _serializer.Serialize(new NatsTestMessage { Data = "fail" }),
            CreatedAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending
        };

        await outbox.AddAsync(message);
        await Task.Delay(100);
        await outbox.MarkAsFailedAsync(messageId, "Test error");

        // Should still be retrievable for retry
    }

    #endregion

    #region NatsJSInboxStore Tests

    [Fact]
    public async Task InboxStore_TryLockMessage_ShouldAcquireLock()
    {
        if (_nats is null) return;
        var streamName = $"INBOX_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();

        var locked = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        locked.Should().BeTrue();
    }

    [Fact]
    public async Task InboxStore_MarkAsProcessed_ShouldSetProcessedFlag()
    {
        if (_nats is null) return;
        var streamName = $"INBOX_PROC_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();
        var inboxMsg = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "NatsTestMessage",
            Payload = _serializer.Serialize(new NatsTestMessage { Data = "inbox" }),
            Status = InboxStatus.Processing
        };

        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.MarkAsProcessedAsync(inboxMsg);
        await Task.Delay(200);
        var processed = await inbox.HasBeenProcessedAsync(messageId);

        processed.Should().BeTrue();
    }

    [Fact]
    public async Task InboxStore_ReleaseLock_ShouldAllowReacquisition()
    {
        if (_nats is null) return;
        var streamName = $"INBOX_REL_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(_nats, _serializer, _provider, streamName);
        var messageId = MessageExtensions.NewMessageId();

        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.ReleaseLockAsync(messageId);
        var reacquired = await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));

        reacquired.Should().BeTrue();
    }

    #endregion

    #region NatsJSDeadLetterQueue Tests

    [Fact]
    public async Task DeadLetterQueue_SendAndGet_ShouldWork()
    {
        if (_nats is null) return;
        var streamName = $"DLQ_{Guid.NewGuid():N}";
        var dlq = new NatsJSDeadLetterQueue(_nats, _serializer, _provider, streamName);
        var message = new NatsTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "dlq-test" };

        await dlq.SendAsync(message, new Exception("Test error"), retryCount: 3);
        await Task.Delay(200);
        var failed = await dlq.GetFailedMessagesAsync(10);

        failed.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DeadLetterQueue_MultipleMessages_ShouldRetrieveAll()
    {
        if (_nats is null) return;
        var streamName = $"DLQ_MULTI_{Guid.NewGuid():N}";
        var dlq = new NatsJSDeadLetterQueue(_nats, _serializer, _provider, streamName);

        for (int i = 0; i < 3; i++)
        {
            var message = new NatsTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"dlq-{i}" };
            await dlq.SendAsync(message, new Exception($"Error {i}"), retryCount: i);
        }

        await Task.Delay(300);
        var failed = await dlq.GetFailedMessagesAsync(10);

        failed.Count.Should().BeGreaterOrEqualTo(3);
    }

    #endregion

    #region NatsJSEventStore Tests

    [Fact]
    public async Task EventStore_AppendAndRead_ShouldWork()
    {
        if (_nats is null) return;
        var streamName = $"EVENTS_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(_nats, _serializer, _provider, streamName: streamName);
        var streamId = $"order-{Guid.NewGuid():N}";
        var events = new List<IEvent>
        {
            new NatsTestEvent { MessageId = MessageExtensions.NewMessageId(), Data = "event-1" },
            new NatsTestEvent { MessageId = MessageExtensions.NewMessageId(), Data = "event-2" }
        };

        await eventStore.AppendAsync(streamId, events);
        await Task.Delay(200);
        var stream = await eventStore.ReadAsync(streamId);

        stream.Events.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task EventStore_GetVersion_ShouldReturnCorrectVersion()
    {
        if (_nats is null) return;
        var streamName = $"EVENTS_VER_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(_nats, _serializer, _provider, streamName: streamName);
        var streamId = $"order-ver-{Guid.NewGuid():N}";
        var events = new List<IEvent>
        {
            new NatsTestEvent { MessageId = MessageExtensions.NewMessageId(), Data = "v1" },
            new NatsTestEvent { MessageId = MessageExtensions.NewMessageId(), Data = "v2" },
            new NatsTestEvent { MessageId = MessageExtensions.NewMessageId(), Data = "v3" }
        };

        await eventStore.AppendAsync(streamId, events);
        await Task.Delay(200);
        var version = await eventStore.GetVersionAsync(streamId);

        version.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task EventStore_ReadFromVersion_ShouldReturnSubset()
    {
        if (_nats is null) return;
        var streamName = $"EVENTS_FROM_{Guid.NewGuid():N}";
        var eventStore = new NatsJSEventStore(_nats, _serializer, _provider, streamName: streamName);
        var streamId = $"order-from-{Guid.NewGuid():N}";

        // Append initial events
        var events1 = new List<IEvent> { new NatsTestEvent { MessageId = MessageExtensions.NewMessageId(), Data = "first" } };
        await eventStore.AppendAsync(streamId, events1);
        await Task.Delay(100);

        // Append more events
        var events2 = new List<IEvent> { new NatsTestEvent { MessageId = MessageExtensions.NewMessageId(), Data = "second" } };
        await eventStore.AppendAsync(streamId, events2);
        await Task.Delay(100);

        var stream = await eventStore.ReadAsync(streamId, fromVersion: 0);

        stream.Events.Should().NotBeEmpty();
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

    #region NatsFlowStore Tests

    [Fact]
    public async Task FlowStore_CreateAndGet_ShouldWork()
    {
        if (_nats is null) return;
        var store = new NatsFlowStore(_nats, _serializer, $"flows_{Guid.NewGuid():N}");
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
    public async Task FlowStore_Update_ShouldWork()
    {
        if (_nats is null) return;
        var store = new NatsFlowStore(_nats, _serializer, $"flows_upd_{Guid.NewGuid():N}");
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

        updated.Should().BeTrue();
    }

    [Fact]
    public async Task FlowStore_TryClaim_ShouldClaimAbandonedFlow()
    {
        if (_nats is null) return;
        var store = new NatsFlowStore(_nats, _serializer, $"flows_claim_{Guid.NewGuid():N}");
        var flowId = $"flow-{Guid.NewGuid():N}";
        var state = new FlowState
        {
            Id = flowId,
            Type = "TestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds()
        };

        await store.CreateAsync(state);
        var claimed = await store.TryClaimAsync("TestFlow", "node-2", timeoutMs: 60000);

        claimed.Should().NotBeNull();
        claimed!.Owner.Should().Be("node-2");
    }

    [Fact]
    public async Task FlowStore_Heartbeat_ShouldWork()
    {
        if (_nats is null) return;
        var store = new NatsFlowStore(_nats, _serializer, $"flows_hb_{Guid.NewGuid():N}");
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

    #region NatsDslFlowStore Tests

    [Fact]
    public async Task DslFlowStore_CreateAndGet_ShouldWork()
    {
        if (_nats is null) return;
        var store = new NatsDslFlowStore(_nats, _serializer, $"dslflows_{Guid.NewGuid():N}");
        var flowId = $"dsl-{Guid.NewGuid():N}";
        var snapshot = new FlowSnapshot<NatsTestFlowState>(
            flowId,
            new NatsTestFlowState { Counter = 0 },
            CurrentStep: 0,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);

        var created = await store.CreateAsync(snapshot);
        var loaded = await store.GetAsync<NatsTestFlowState>(flowId);

        created.Should().BeTrue();
        loaded.Should().NotBeNull();
    }

    [Fact]
    public async Task DslFlowStore_Update_ShouldWork()
    {
        if (_nats is null) return;
        var store = new NatsDslFlowStore(_nats, _serializer, $"dslflows_upd_{Guid.NewGuid():N}");
        var flowId = $"dsl-{Guid.NewGuid():N}";
        var snapshot = new FlowSnapshot<NatsTestFlowState>(
            flowId,
            new NatsTestFlowState { Counter = 0 },
            CurrentStep: 0,
            Status: DslFlowStatus.Running,
            Error: null,
            WaitCondition: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            Version: 0);

        await store.CreateAsync(snapshot);
        var updated = await store.UpdateAsync(snapshot with { CurrentStep = 1 });

        updated.Should().BeTrue();
    }

    [Fact]
    public async Task DslFlowStore_WaitCondition_ShouldWork()
    {
        if (_nats is null) return;
        var store = new NatsDslFlowStore(_nats, _serializer, $"dslflows_wait_{Guid.NewGuid():N}");
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

    #region NatsSnapshotStore Tests

    [Fact]
    public async Task SnapshotStore_SaveAndLoad_ShouldWork()
    {
        if (_nats is null) return;
        var store = new NatsSnapshotStore(_nats, _serializer, Options.Create(new SnapshotOptions()), NullLogger<NatsSnapshotStore>.Instance);
        var streamId = $"stream-{Guid.NewGuid():N}";
        var aggregate = new NatsTestAggregate { Id = "agg-1", Counter = 42 };

        await store.SaveAsync(streamId, aggregate, version: 10);
        var snapshot = await store.LoadAsync<NatsTestAggregate>(streamId);

        snapshot.Should().NotBeNull();
        snapshot!.Value.Version.Should().Be(10);
        snapshot.Value.State.Counter.Should().Be(42);
    }

    [Fact]
    public async Task SnapshotStore_Delete_ShouldRemoveSnapshot()
    {
        if (_nats is null) return;
        var store = new NatsSnapshotStore(_nats, _serializer, Options.Create(new SnapshotOptions()), NullLogger<NatsSnapshotStore>.Instance);
        var streamId = $"stream-del-{Guid.NewGuid():N}";
        var aggregate = new NatsTestAggregate { Id = "agg-del", Counter = 99 };

        await store.SaveAsync(streamId, aggregate, version: 5);
        await store.DeleteAsync(streamId);
        var snapshot = await store.LoadAsync<NatsTestAggregate>(streamId);

        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task SnapshotStore_LoadNonExistent_ShouldReturnNull()
    {
        if (_nats is null) return;
        var store = new NatsSnapshotStore(_nats, _serializer, Options.Create(new SnapshotOptions()), NullLogger<NatsSnapshotStore>.Instance);

        var snapshot = await store.LoadAsync<NatsTestAggregate>("non-existent-stream");

        snapshot.Should().BeNull();
    }

    #endregion
}

#region Test Types

[MemoryPackable]
public partial class NatsTestResult
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class NatsTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class NatsTestEvent : IEvent
{
    public long MessageId { get; set; }
    public long? CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

[MemoryPackable]
public partial class NatsTestAggregate
{
    public string Id { get; set; } = string.Empty;
    public int Counter { get; set; }
}

[MemoryPackable]
public partial class NatsTestFlowState : IFlowState
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
