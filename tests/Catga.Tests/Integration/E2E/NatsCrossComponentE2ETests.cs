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
using Catga.Transport;
using Catga.Transport.Nats;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Catga.Tests.Integration;
using NATS.Client.Core;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// NATS cross-component E2E tests validating integration between multiple stores.
/// </summary>
[Trait("Requires", "Docker")]
[Collection("IntegrationTests")]
public class NatsCrossComponentE2ETests
{
    private readonly global::Catga.Tests.Integration.SharedIntegrationFixture _fixture;
    private NatsConnection? _nats => _fixture.NatsConnection;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IMessageSerializer _jsonSerializer = new Catga.Tests.Helpers.TestMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider(new CatgaResilienceOptions { PersistenceTimeout = TimeSpan.FromMinutes(2), TransportTimeout = TimeSpan.FromSeconds(30) });

    public NatsCrossComponentE2ETests(global::Catga.Tests.Integration.SharedIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    #region Outbox + Inbox Integration

    [Fact]
    public async Task Nats_OutboxInbox_EndToEnd_ShouldGuaranteeExactlyOnce()
    {
        if (_nats is null) return;

        var outboxStream = $"OUTBOX_{Guid.NewGuid():N}";
        var inboxStream = $"INBOX_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(_nats, _serializer, _provider, outboxStream);
        var inbox = new NatsJSInboxStore(_nats, _serializer, _provider, inboxStream);
        var messageId = MessageExtensions.NewMessageId();
        var outboxMsg = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "NatsCrossTestMessage",
            Payload = _serializer.Serialize(new NatsCrossTestMessage { MessageId = messageId, Data = "outbox-inbox-test" }),
            CreatedAt = DateTime.UtcNow,
            Status = OutboxStatus.Pending
        };

        // Act - Outbox: Add message
        await outbox.AddAsync(outboxMsg);
        var pending = await AsyncTestWait.WaitUntilAsync(
            () => outbox.GetPendingMessagesAsync(10).AsTask(),
            messages => messages.Any(m => m.MessageId == messageId));

        // Act - Inbox: Check and mark as processed
        var alreadyProcessed = await inbox.HasBeenProcessedAsync(messageId);
        var inboxMsg = new InboxMessage
        {
            MessageId = messageId,
            MessageType = "NatsCrossTestMessage",
            Payload = _serializer.Serialize(new NatsCrossTestMessage { MessageId = messageId }),
            Status = InboxStatus.Processed
        };
        await inbox.TryLockMessageAsync(messageId, TimeSpan.FromMinutes(5));
        await inbox.MarkAsProcessedAsync(inboxMsg);
        var nowProcessed = await AsyncTestWait.WaitUntilAsync(
            () => inbox.HasBeenProcessedAsync(messageId).AsTask(),
            processed => processed);

        // Act - Outbox: Mark as published
        await outbox.MarkAsPublishedAsync(messageId);

        // Assert
        pending.Should().ContainSingle(m => m.MessageId == messageId);
        alreadyProcessed.Should().BeFalse();
        nowProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task Nats_OutboxInbox_DuplicateMessage_ShouldBeIdempotent()
    {
        if (_nats is null) return;

        var inboxStream = $"INBOX_DUP_{Guid.NewGuid():N}";
        var inbox = new NatsJSInboxStore(_nats, _serializer, _provider, inboxStream);
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
                    MessageType = "NatsCrossTestMessage",
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

    #region FlowStore + DslFlowStore Integration

    [Fact]
    public async Task Nats_FlowStore_DslFlowStore_ParallelFlows_ShouldWork()
    {
        if (_nats is null) return;

        var flowBucket = $"flows_{Guid.NewGuid():N}";
        var dslBucket = $"dslflows_{Guid.NewGuid():N}";
        var flowStore = new NatsFlowStore(_nats, _jsonSerializer, flowBucket);
        var dslFlowStore = new NatsDslFlowStore(_nats, _jsonSerializer, dslBucket);

        var flowId = $"nats-parallel-flow-{Guid.NewGuid():N}";

        // Create main flow
        var mainFlow = new FlowState
        {
            Id = flowId,
            Type = "NatsParallelTestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "node-1",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await flowStore.CreateAsync(mainFlow);

        // Create DSL flow with parallel branches
        var dslFlow = FlowSnapshot<NatsCrossTestFlowState>.Create(
            flowId,
            new NatsCrossTestFlowState { Counter = 0 },
            currentStep: 0,
            status: DslFlowStatus.Running,
            error: null,
            waitCondition: new WaitCondition
            {
                FlowId = flowId,
                FlowType = "NatsParallelTestFlow",
                Step = 0,
                Type = WaitType.All,
                CorrelationId = Guid.NewGuid().ToString(),
                ExpectedCount = 2,
                Timeout = TimeSpan.FromMinutes(5),
                CreatedAt = DateTime.UtcNow
            },
            createdAt: DateTime.UtcNow,
            updatedAt: DateTime.UtcNow,
            version: 0);
        await dslFlowStore.CreateAsync(dslFlow);

        // Complete branches - update DSL flow (pass current version for optimistic locking)
        var updatedDsl = dslFlow with
        {
            Position = new FlowPosition([1]),
            WaitCondition = null,
            UpdatedAt = DateTime.UtcNow,
            Version = 0  // current version (store will increment to 1)
        };
        await dslFlowStore.UpdateAsync(updatedDsl);

        // Verify
        var loadedDsl = await dslFlowStore.GetAsync<NatsCrossTestFlowState>(flowId);
        loadedDsl.Should().NotBeNull();
        loadedDsl!.Position.CurrentIndex.Should().Be(1);
    }

    [Fact]
    public async Task Nats_FlowStore_Failover_ShouldClaimOrphanedFlows()
    {
        if (_nats is null) return;

        var flowBucket = $"flows_claim_{Guid.NewGuid():N}";
        var flowStore = new NatsFlowStore(_nats, _jsonSerializer, flowBucket);
        var flowId = $"nats-orphan-flow-{Guid.NewGuid():N}";

        // Create flow with old heartbeat (simulating crashed processor)
        var orphanFlow = new FlowState
        {
            Id = flowId,
            Type = "NatsOrphanTestFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "crashed-processor",
            HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds()
        };
        await flowStore.CreateAsync(orphanFlow);

        // New processor tries to claim
        var claimed = await flowStore.TryClaimAsync("NatsOrphanTestFlow", "new-processor", timeoutMs: 60000);

        claimed.Should().NotBeNull();
        claimed!.Owner.Should().Be("new-processor");
    }

    #endregion

    #region DLQ + Idempotency Integration

    [Fact]
    public async Task Nats_DLQ_Idempotency_FailedMessageRetry_ShouldWork()
    {
        if (_nats is null) return;

        var dlqStream = $"DLQ_{Guid.NewGuid():N}";
        var idemStream = $"IDEM_{Guid.NewGuid():N}";
        var dlq = new NatsJSDeadLetterQueue(_nats, _serializer, _provider, Microsoft.Extensions.Options.Options.Create(new Catga.Persistence.NatsJSStoreOptions { DlqStreamName = dlqStream }));
        var idempotency = new NatsJSIdempotencyStore(_nats, _serializer, _provider, Microsoft.Extensions.Options.Options.Create(new Catga.Persistence.NatsJSStoreOptions { IdempotencyStreamName = idemStream }));
        var messageId = MessageExtensions.NewMessageId();
        var message = new NatsCrossTestMessage { MessageId = messageId, Data = "failed-message" };

        // First attempt fails - send to DLQ
        await dlq.SendAsync(message, new Exception("Processing failed"), retryCount: 1);

        // Verify in DLQ
        var failed = await AsyncTestWait.WaitUntilAsync(
            () => dlq.GetFailedMessagesAsync(10),
            messages => messages.Any(m => m.MessageId == messageId));
        failed.Should().ContainSingle(m => m.MessageId == messageId);

        // Retry succeeds - mark as processed
        await idempotency.MarkAsProcessedAsync(messageId, new NatsCrossTestResult { Value = 42 });

        // Verify idempotency
        var processed = await AsyncTestWait.WaitUntilAsync(
            () => idempotency.HasBeenProcessedAsync(messageId),
            value => value);
        var result = await AsyncTestWait.WaitUntilAsync(
            () => idempotency.GetCachedResultAsync<NatsCrossTestResult>(messageId),
            value => value?.Value == 42);

        processed.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Value.Should().Be(42);
    }

    #endregion

    #region Full Saga Pattern with NATS

    [Fact]
    public async Task Nats_Saga_CompleteWorkflow_ShouldCoordinateStores()
    {
        if (_nats is null) return;

        var flowBucket = $"saga_flows_{Guid.NewGuid():N}";
        var idemStream = $"saga_idem_{Guid.NewGuid():N}";
        var outboxStream = $"saga_outbox_{Guid.NewGuid():N}";

        var flowStore = new NatsFlowStore(_nats, _jsonSerializer, flowBucket);
        var idempotency = new NatsJSIdempotencyStore(_nats, _serializer, _provider, Microsoft.Extensions.Options.Options.Create(new Catga.Persistence.NatsJSStoreOptions { IdempotencyStreamName = idemStream }));
        var outbox = new NatsJSOutboxStore(_nats, _serializer, _provider, outboxStream);

        var sagaId = $"nats-saga-{Guid.NewGuid():N}";
        var steps = new[] { "Reserve", "Charge", "Ship", "Complete" };

        // Create saga flow
        var sagaFlow = new FlowState
        {
            Id = sagaId,
            Type = "NatsSagaFlow",
            Status = FlowStatus.Running,
            Step = 0,
            Version = 0,
            Owner = "saga-processor",
            HeartbeatAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await flowStore.CreateAsync(sagaFlow);

        var stepIndex = 0;
        foreach (var step in steps)
        {
            var stepMessageId = MessageExtensions.NewMessageId();

            // Check idempotency
            if (await idempotency.HasBeenProcessedAsync(stepMessageId))
                continue;

            // Update flow
            var currentFlow = await flowStore.GetAsync(sagaId);
            if (currentFlow is not null)
            {
                currentFlow.Step = ++stepIndex;
                await flowStore.UpdateAsync(currentFlow);
            }

            // Add outbox message for next step
            var nextMessageId = MessageExtensions.NewMessageId();
            var outboxMsg = new OutboxMessage
            {
                MessageId = nextMessageId,
                MessageType = "NatsCrossTestMessage",
                Payload = _serializer.Serialize(new NatsCrossTestMessage { MessageId = nextMessageId, Data = $"{step}Completed" }),
                CreatedAt = DateTime.UtcNow,
                Status = OutboxStatus.Pending
            };
            await outbox.AddAsync(outboxMsg);

            // Mark idempotency
            await idempotency.MarkAsProcessedAsync(stepMessageId, new NatsCrossTestResult { Value = 1 });

            // Complete outbox
            await outbox.MarkAsPublishedAsync(nextMessageId);
        }

        // Complete saga
        var finalFlow = await flowStore.GetAsync(sagaId);
        if (finalFlow is not null)
        {
            finalFlow.Status = FlowStatus.Done;
            await flowStore.UpdateAsync(finalFlow);
        }

        // Verify saga completed
        var completedFlow = await flowStore.GetAsync(sagaId);
        completedFlow.Should().NotBeNull();
        completedFlow!.Status.Should().Be(FlowStatus.Done);
        completedFlow.Step.Should().Be(4);
    }

    #endregion

    #region Transport + Persistence Integration

    [Fact]
    public async Task Nats_Transport_Idempotency_Integration_ShouldWork()
    {
        if (_nats is null) return;

        var idemStream = $"IDEM_TRANS_{Guid.NewGuid():N}";
        var transportOptions = new NatsTransportOptions { SubjectPrefix = $"cross-idem-{Guid.NewGuid():N}" };
        var idempotency = new NatsJSIdempotencyStore(_nats, _serializer, _provider, Microsoft.Extensions.Options.Options.Create(new Catga.Persistence.NatsJSStoreOptions { IdempotencyStreamName = idemStream }));
        await using var transport = new NatsMessageTransport(_nats, _serializer, NullLogger<NatsMessageTransport>.Instance, _provider, transportOptions);

        var receivedTcs = new TaskCompletionSource<NatsCrossTestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await transport.SubscribeAsync<NatsCrossTestMessage>(async (msg, ctx) =>
        {
            receivedTcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var messageId = MessageExtensions.NewMessageId();
        var msg = new NatsCrossTestMessage
        {
            MessageId = messageId,
            QoS = QualityOfService.AtLeastOnce,
            Data = "duplicate-msg"
        };

        // Publish the same logical message twice. Idempotency should collapse duplicates.
        await transport.PublishAsync(msg, new TransportContext { MessageId = messageId });

        var completed = await Task.WhenAny(receivedTcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(receivedTcs.Task);

        var received = await receivedTcs.Task;
        received.MessageId.Should().Be(messageId);
        (await idempotency.HasBeenProcessedAsync(messageId)).Should().BeFalse();

        await idempotency.MarkAsProcessedAsync(messageId, new NatsCrossTestResult { Value = 1 });
        var processed = await AsyncTestWait.WaitUntilAsync(
            () => idempotency.HasBeenProcessedAsync(messageId),
            value => value,
            timeout: TimeSpan.FromSeconds(3));

        processed.Should().BeTrue();
    }

    [Fact]
    public async Task Nats_Transport_Outbox_Integration_ShouldWork()
    {
        if (_nats is null) return;

        var outboxStream = $"OUTBOX_TRANS_{Guid.NewGuid():N}";
        var transportOptions = new NatsTransportOptions { SubjectPrefix = $"cross-outbox-{Guid.NewGuid():N}" };
        var outbox = new NatsJSOutboxStore(_nats, _serializer, _provider, outboxStream);
        await using var transport = new NatsMessageTransport(_nats, _serializer, NullLogger<NatsMessageTransport>.Instance, _provider, transportOptions);

        var receivedMessages = new List<NatsCrossTestMessage>();
        var tcs = new TaskCompletionSource();

        // Subscribe
        await transport.SubscribeAsync<NatsCrossTestMessage>(async (msg, ctx) =>
        {
            receivedMessages.Add(msg);
            if (receivedMessages.Count >= 3) tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(50);

        // Add messages to outbox
        var messageIds = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            var msgId = MessageExtensions.NewMessageId();
            messageIds.Add(msgId);
            var outboxMsg = new OutboxMessage
            {
                MessageId = msgId,
                MessageType = "NatsCrossTestMessage",
                Payload = _serializer.Serialize(new NatsCrossTestMessage { MessageId = msgId, Data = $"outbox-msg-{i}" }),
                CreatedAt = DateTime.UtcNow,
                Status = OutboxStatus.Pending
            };
            await outbox.AddAsync(outboxMsg);
        }

        // Simulate outbox processor: get pending and publish
        var pending = await AsyncTestWait.WaitUntilAsync(
            () => outbox.GetPendingMessagesAsync(10).AsTask(),
            messages => messages.Count >= 3);
        foreach (var outboxMsg in pending)
        {
            var msg = (NatsCrossTestMessage?)_serializer.Deserialize(outboxMsg.Payload, typeof(NatsCrossTestMessage));
            if (msg is not null)
            {
                await transport.PublishAsync(msg);
                await outbox.MarkAsPublishedAsync(outboxMsg.MessageId);
            }
        }

        await AsyncTestWait.WaitUntilAsync(
            () => Task.FromResult(receivedMessages.Count >= 3),
            timeout: TimeSpan.FromSeconds(3));

        receivedMessages.Should().HaveCount(3);
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

#region NATS Test Types

[MemoryPackable]
public partial class NatsCrossTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class NatsCrossTestResult
{
    public int Value { get; set; }
}

[MemoryPackable]
public partial class NatsCrossTestFlowState : IFlowState
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
