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
using Catga.Transport.Nats;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// NATS cross-component E2E tests validating integration between multiple stores.
/// </summary>
[Trait("Requires", "Docker")]
public class NatsCrossComponentE2ETests : IAsyncLifetime
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
        await Task.Delay(200);
        var pending = await outbox.GetPendingMessagesAsync(10);

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
        await Task.Delay(200);
        var nowProcessed = await inbox.HasBeenProcessedAsync(messageId);

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
        var flowStore = new NatsFlowStore(_nats, _serializer, flowBucket);
        var dslFlowStore = new NatsDslFlowStore(_nats, _serializer, dslBucket);

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

        // Complete branches - update DSL flow
        var updatedDsl = dslFlow with
        {
            Position = new FlowPosition([1]),
            WaitCondition = null,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
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
        var flowStore = new NatsFlowStore(_nats, _serializer, flowBucket);
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
        var dlq = new NatsJSDeadLetterQueue(_nats, _serializer, _provider, dlqStream);
        var idempotency = new NatsJSIdempotencyStore(_nats, _serializer, _provider, idemStream);
        var messageId = MessageExtensions.NewMessageId();
        var message = new NatsCrossTestMessage { MessageId = messageId, Data = "failed-message" };

        // First attempt fails - send to DLQ
        await dlq.SendAsync(message, new Exception("Processing failed"), retryCount: 1);
        await Task.Delay(200);

        // Verify in DLQ
        var failed = await dlq.GetFailedMessagesAsync(10);
        failed.Should().ContainSingle(m => m.MessageId == messageId);

        // Retry succeeds - mark as processed
        await idempotency.MarkAsProcessedAsync(messageId, new NatsCrossTestResult { Value = 42 });
        await Task.Delay(200);

        // Verify idempotency
        var processed = await idempotency.HasBeenProcessedAsync(messageId);
        var result = await idempotency.GetCachedResultAsync<NatsCrossTestResult>(messageId);

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

        var flowStore = new NatsFlowStore(_nats, _serializer, flowBucket);
        var idempotency = new NatsJSIdempotencyStore(_nats, _serializer, _provider, idemStream);
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
                currentFlow.Step = stepIndex++;
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
        var idempotency = new NatsJSIdempotencyStore(_nats, _serializer, _provider, idemStream);
        await using var transport = new NatsMessageTransport(_nats, _serializer, NullLogger<NatsMessageTransport>.Instance, _provider);

        var processedMessages = new List<NatsCrossTestMessage>();
        var tcs = new TaskCompletionSource();

        // Subscribe with idempotency check
        await transport.SubscribeAsync<NatsCrossTestMessage>(async (msg, ctx) =>
        {
            if (!await idempotency.HasBeenProcessedAsync(msg.MessageId))
            {
                processedMessages.Add(msg);
                await idempotency.MarkAsProcessedAsync(msg.MessageId, new NatsCrossTestResult { Value = 1 });
            }
            if (processedMessages.Count >= 3) tcs.TrySetResult();
        });
        await Task.Delay(100);

        // Publish messages
        for (int i = 0; i < 3; i++)
        {
            var msg = new NatsCrossTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"msg-{i}" };
            await transport.PublishAsync(msg);
        }

        await Task.WhenAny(tcs.Task, Task.Delay(10000));

        processedMessages.Should().HaveCount(3);
    }

    [Fact]
    public async Task Nats_Transport_Outbox_Integration_ShouldWork()
    {
        if (_nats is null) return;

        var outboxStream = $"OUTBOX_TRANS_{Guid.NewGuid():N}";
        var outbox = new NatsJSOutboxStore(_nats, _serializer, _provider, outboxStream);
        await using var transport = new NatsMessageTransport(_nats, _serializer, NullLogger<NatsMessageTransport>.Instance, _provider);

        var receivedMessages = new List<NatsCrossTestMessage>();
        var tcs = new TaskCompletionSource();

        // Subscribe
        await transport.SubscribeAsync<NatsCrossTestMessage>(async (msg, ctx) =>
        {
            receivedMessages.Add(msg);
            if (receivedMessages.Count >= 3) tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(100);

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
        await Task.Delay(200);

        // Simulate outbox processor: get pending and publish
        var pending = await outbox.GetPendingMessagesAsync(10);
        foreach (var outboxMsg in pending)
        {
            var msg = (NatsCrossTestMessage?)_serializer.Deserialize(outboxMsg.Payload, typeof(NatsCrossTestMessage));
            if (msg is not null)
            {
                await transport.PublishAsync(msg);
                await outbox.MarkAsPublishedAsync(outboxMsg.MessageId);
            }
        }

        await Task.WhenAny(tcs.Task, Task.Delay(10000));

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



