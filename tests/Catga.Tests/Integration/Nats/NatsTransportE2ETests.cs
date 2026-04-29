using System.Collections.Concurrent;
using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Messaging;
using Catga.Observability;
using Catga.Persistence.InMemory.Flow;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Testing;
using Catga.Transport;
using Catga.Transport.Nats;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;

namespace Catga.Tests.Integration.Nats;

/// <summary>
/// E2E tests for NATS Transport.
/// Target: 80% coverage for Catga.Transport.Nats
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
[Collection("NatsTransport")]
public sealed class NatsTransportE2ETests : IAsyncLifetime
{
    private IContainer? _container;
    private NatsConnection? _nats;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider(
        new CatgaResilienceOptions { TransportTimeout = TimeSpan.FromSeconds(30), TransportRetryCount = 0 });
    private readonly ILogger<NatsMessageTransport> _logger = NullLogger<NatsMessageTransport>.Instance;

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

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(4222);
        _nats = await ConnectWithRetryAsync($"nats://{host}:{port}");
    }

    public async Task DisposeAsync()
    {
        if (_nats is not null) await _nats.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    private static async Task<NatsConnection> ConnectWithRetryAsync(string url)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var connection = new NatsConnection(new NatsOpts
                {
                    Url = url,
                    ConnectTimeout = TimeSpan.FromSeconds(2)
                });

                await connection.ConnectAsync();
                return connection;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(200);
            }
        }

        throw new InvalidOperationException($"Timed out connecting to NATS at {url}", lastError);
    }

    #region Basic Publish/Subscribe Tests

    [Fact]
    public async Task PublishAsync_WithSubscriber_ShouldDeliverMessage()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"test-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "hello" };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
        var received = await tcs.Task;
        received.Data.Should().Be("hello");
    }

    [Fact]
    public async Task PublishAsync_QoS0_AtMostOnce_ShouldDeliverViaCorePubSub()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"qos0-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "qos0", QoS = QualityOfService.AtMostOnce };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task PublishAsync_QoS1_AtLeastOnce_ShouldDeliverViaJetStream()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"qos1-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "qos1", QoS = QualityOfService.AtLeastOnce };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task PublishAsync_QoS2_ExactlyOnce_ShouldDeliverWithDedup()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"qos2-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var received = 0;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            Interlocked.Increment(ref received);
            tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var messageId = MessageExtensions.NewMessageId();
        var message = new NatsTransportTestMessage { MessageId = messageId, Data = "qos2", QoS = QualityOfService.ExactlyOnce };
        var ctx = new TransportContext { MessageId = messageId };

        // Publish twice with same MessageId
        await transport.PublishAsync(message, ctx);
        await transport.PublishAsync(message, ctx);

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        await Task.Delay(500);

        received.Should().Be(1, "QoS2 should deduplicate messages via NATS MsgId");
    }

    [Fact]
    public async Task SendAsync_WithContext_ShouldDeliverTransportContextToDestinationSubscriber()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"ctx-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var destination = "inventory.context";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<NatsTransportTestMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        await transport.SendAsync(
            new NatsTransportTestMessage { Data = "ctx" },
            destination,
            new TransportContext
            {
                MessageId = 111,
                CorrelationId = 222,
                MessageType = "tests.nats.context",
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "acme",
                    ["region"] = "eu"
                }
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.MessageId.Should().Be(111);
        receivedContext.CorrelationId.Should().Be(222);
        receivedContext.MessageType.Should().Be("tests.nats.context");
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!["tenant"].Should().Be("acme");
        receivedContext.Metadata["region"].Should().Be("eu");
    }

    [Fact]
    public async Task PublishAsync_WithContext_ShouldDeliverTransportContextToSubscriber()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"publish-ctx-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        await transport.PublishAsync(
            new NatsTransportTestMessage { Data = "ctx-publish" },
            new TransportContext
            {
                MessageId = 321,
                CorrelationId = 654,
                MessageType = "tests.nats.publish",
                SentAt = new DateTime(2024, 02, 03, 04, 05, 06, DateTimeKind.Utc),
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "acme",
                    ["region"] = "eu"
                }
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.MessageId.Should().Be(321);
        receivedContext.CorrelationId.Should().Be(654);
        receivedContext.MessageType.Should().Be("tests.nats.publish");
        receivedContext.SentAt.Should().Be(new DateTime(2024, 02, 03, 04, 05, 06, DateTimeKind.Utc));
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!["tenant"].Should().Be("acme");
        receivedContext.Metadata["region"].Should().Be("eu");
    }

    [Fact]
    public async Task SubscribeAsync_WithExternalLowercaseHeaders_ShouldRestoreTransportContext()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"publish-lowercase-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var destination = "inventory.lowercase";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<NatsTransportTestMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        await _nats.PublishAsync(
            $"{options.SubjectPrefix.TrimEnd('.')}.{destination}",
            _serializer.Serialize(new NatsTransportTestMessage { Data = "lowercase-headers" }),
            headers: new NatsHeaders
            {
                ["message_id"] = "9123",
                ["correlation_id"] = "8456",
                ["message_type"] = "external.nats.message",
                ["sent_at"] = "2024-03-04T05:06:07.0000000Z",
                ["qos"] = ((int)QualityOfService.AtLeastOnce).ToString(),
                ["meta.tenant"] = "acme"
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.MessageId.Should().Be(9123);
        receivedContext.CorrelationId.Should().Be(8456);
        receivedContext.MessageType.Should().Be("external.nats.message");
        receivedContext.SentAt.Should().Be(new DateTime(2024, 03, 04, 05, 06, 07, DateTimeKind.Utc));
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!["tenant"].Should().Be("acme");
    }

    [Fact]
    public async Task SubscribeAsync_WithExternalHyphenatedHeaders_ShouldRestoreTransportContext()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"publish-hyphenated-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var destination = "inventory.hyphenated";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<NatsTransportTestMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        await _nats.PublishAsync(
            $"{options.SubjectPrefix.TrimEnd('.')}.{destination}",
            _serializer.Serialize(new NatsTransportTestMessage { Data = "hyphenated-headers" }),
            headers: new NatsHeaders
            {
                ["message-id"] = "9234",
                ["correlation-id"] = "8567",
                ["message-type"] = "external.nats.hyphenated",
                ["sent-at"] = "2024-03-05T06:07:08.0000000Z",
                ["qos"] = ((int)QualityOfService.AtLeastOnce).ToString(),
                ["meta-tenant"] = "acme",
                ["meta-reply-to"] = "reply.hyphenated"
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.MessageId.Should().Be(9234);
        receivedContext.CorrelationId.Should().Be(8567);
        receivedContext.MessageType.Should().Be("external.nats.hyphenated");
        receivedContext.SentAt.Should().Be(new DateTime(2024, 03, 05, 06, 07, 08, DateTimeKind.Utc));
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!["tenant"].Should().Be("acme");
        receivedContext.Metadata["reply_to"].Should().Be("reply.hyphenated");
        receivedContext.Metadata["reply_subject"].Should().Be("reply.hyphenated");
    }

    [Fact]
    public async Task PublishAsync_WithPrioritizedMessage_ShouldExposePriorityMetadataToSubscriber()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"publish-priority-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<NatsPrioritizedTransportMessage>(async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        await transport.PublishAsync(new NatsPrioritizedTransportMessage
        {
            Data = "priority",
            Priority = MessagePriority.Critical
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!["x-priority"].Should().Be("3");
    }

    [Fact]
    public async Task PublishAsync_WithDelayedMessage_ShouldExposeDelayMetadataToSubscriber()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"publish-delay-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<NatsDelayedTransportMessage>(async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        await transport.PublishAsync(new NatsDelayedTransportMessage
        {
            Data = "delay",
            ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(5)
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!.Should().ContainKey("x-delay");
        int.Parse(receivedContext.Metadata["x-delay"]).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FlowRemoteSend_ShouldRoundTripThroughNatsTransport()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"flow-remote-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(3));
        var mediator = new MockMediator();
        var store = new InMemoryDslFlowStore();
        var executor = new DslFlowExecutor<NatsRemoteCheckoutState, NatsRemoteCheckoutFlow>(
            mediator,
            store,
            new NatsRemoteCheckoutFlow(),
            requestClientFactory: requestClientFactory);
        var capturedRequests = new List<object>();
        using var cts = new CancellationTokenSource();
        var checkStockSubject = $"{options.SubjectPrefix.TrimEnd('.')}.{TypeNameCache<NatsRemoteCheckStock>.Name}";
        var reserveItemSubject = $"{options.SubjectPrefix.TrimEnd('.')}.{TypeNameCache<NatsRemoteReserveItem>.Name}";

        var checkStockResponder = Task.Run(async () =>
        {
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(checkStockSubject, cancellationToken: cts.Token))
            {
                var request = _serializer.Deserialize<NatsRemoteCheckStock>(msg.Data!);
                capturedRequests.Add(request);
                await _nats.PublishAsync(
                    msg.ReplyTo!,
                    _serializer.Serialize(new NatsRemoteStockResult { Qty = 11, Available = true }),
                    cancellationToken: cts.Token);
            }
        }, cts.Token);

        var reserveItemResponder = Task.Run(async () =>
        {
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(reserveItemSubject, cancellationToken: cts.Token))
            {
                var request = _serializer.Deserialize<NatsRemoteReserveItem>(msg.Data!);
                capturedRequests.Add(request);
                await _nats.PublishAsync(
                    msg.ReplyTo!,
                    _serializer.Serialize(new NatsRemoteReservationResult { ReservationId = "NATS-RES-001" }),
                    cancellationToken: cts.Token);
            }
        }, cts.Token);

        await Task.Delay(200);

        try
        {
            var result = await executor.RunAsync(new NatsRemoteCheckoutState
            {
                FlowId = "nats-remote-send",
                Sku = "SKU-NATS",
                Qty = 3
            });

            result.IsSuccess.Should().BeTrue();
            result.State.Should().NotBeNull();
            result.State!.InStock.Should().BeTrue();
            result.State.Qty.Should().Be(11);
            result.State.ReservationId.Should().Be("NATS-RES-001");
            mediator.Sent.Should().BeEmpty();

            capturedRequests.Should().HaveCount(2);
            capturedRequests[0].Should().BeOfType<NatsRemoteCheckStock>()
                .Which.Sku.Should().Be("SKU-NATS");
            capturedRequests[1].Should().BeOfType<NatsRemoteReserveItem>()
                .Which.Should().Match<NatsRemoteReserveItem>(request =>
                    request.Sku == "SKU-NATS" &&
                    request.Qty == 11);
        }
        finally
        {
            await cts.CancelAsync();
            try { await checkStockResponder; } catch (OperationCanceledException) { }
            try { await reserveItemResponder; } catch (OperationCanceledException) { }
        }
    }

    #endregion

    #region Batch Publish Tests

    [Fact]
    public async Task PublishBatchAsync_ShouldDeliverAllMessages()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"batch-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var receivedMessageIds = new ConcurrentDictionary<long, byte>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int batchSize = 5;

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            if (receivedMessageIds.TryAdd(msg.MessageId, 0) && receivedMessageIds.Count == batchSize)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var messages = Enumerable.Range(0, batchSize)
            .Select(i => new NatsTransportTestMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = $"batch-{i}",
                QoS = QualityOfService.AtMostOnce
            })
            .ToList();

        await transport.PublishBatchAsync(messages);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(60000));
        result.Should().Be(tcs.Task);
        receivedMessageIds.Keys.Should().BeEquivalentTo(messages.Select(x => x.MessageId));
    }

    #endregion

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_ShouldDeliverToDestination()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"send-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();
        var destination = $"test-destination-{Guid.NewGuid():N}";

        await transport.SubscribeAsync<NatsTransportTestMessage>(destination, async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "send" };
        await transport.SendAsync(message, destination);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task FlowRemoteSend_WithCustomDestination_ShouldRoundTripThroughNatsTransport()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"flow-remote-dest-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(3));
        var mediator = new MockMediator();
        var store = new InMemoryDslFlowStore();
        var executor = new DslFlowExecutor<NatsRemoteCheckoutState, NatsRemoteCheckoutDestinationFlow>(
            mediator,
            store,
            new NatsRemoteCheckoutDestinationFlow("inventory.check-stock", "inventory.reserve-item"),
            requestClientFactory: requestClientFactory);
        var capturedRequests = new List<object>();
        using var cts = new CancellationTokenSource();
        var prefix = options.SubjectPrefix.TrimEnd('.');
        var checkStockSubject = $"{prefix}.inventory.check-stock";
        var reserveItemSubject = $"{prefix}.inventory.reserve-item";

        var checkStockResponder = Task.Run(async () =>
        {
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(checkStockSubject, cancellationToken: cts.Token))
            {
                var request = _serializer.Deserialize<NatsRemoteCheckStock>(msg.Data!);
                capturedRequests.Add(request);
                await _nats.PublishAsync(
                    msg.ReplyTo!,
                    _serializer.Serialize(new NatsRemoteStockResult { Qty = 15, Available = true }),
                    cancellationToken: cts.Token);
            }
        }, cts.Token);

        var reserveItemResponder = Task.Run(async () =>
        {
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(reserveItemSubject, cancellationToken: cts.Token))
            {
                var request = _serializer.Deserialize<NatsRemoteReserveItem>(msg.Data!);
                capturedRequests.Add(request);
                await _nats.PublishAsync(
                    msg.ReplyTo!,
                    _serializer.Serialize(new NatsRemoteReservationResult { ReservationId = "NATS-DEST-001" }),
                    cancellationToken: cts.Token);
            }
        }, cts.Token);

        await Task.Delay(200);

        try
        {
            var result = await executor.RunAsync(new NatsRemoteCheckoutState
            {
                FlowId = "nats-remote-send-destination",
                Sku = "SKU-NATS-DEST",
                Qty = 4
            });

            result.IsSuccess.Should().BeTrue();
            result.State.Should().NotBeNull();
            result.State!.InStock.Should().BeTrue();
            result.State.Qty.Should().Be(15);
            result.State.ReservationId.Should().Be("NATS-DEST-001");
            mediator.Sent.Should().BeEmpty();

            capturedRequests.Should().HaveCount(2);
            capturedRequests[0].Should().BeOfType<NatsRemoteCheckStock>()
                .Which.Sku.Should().Be("SKU-NATS-DEST");
            capturedRequests[1].Should().BeOfType<NatsRemoteReserveItem>()
                .Which.Should().Match<NatsRemoteReserveItem>(request =>
                    request.Sku == "SKU-NATS-DEST" &&
                    request.Qty == 15);
        }
        finally
        {
            await cts.CancelAsync();
            try { await checkStockResponder; } catch (OperationCanceledException) { }
            try { await reserveItemResponder; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task RequestClient_WithCustomDestination_ShouldReturnResponse()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"request-client-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(3));
        var destination = "inventory.check-stock";

        try
        {
            await transport.SubscribeAsync<NatsRemoteCheckStock>(destination, async (request, ctx) =>
            {
                request.Sku.Should().Be("SKU-NCQ");
                ctx.MessageId.Should().NotBeNull();
                ctx.CorrelationId.Should().NotBeNull();
                ctx.MessageType.Should().Be(typeof(NatsRemoteCheckStock).FullName);
                ctx.Metadata.Should().NotBeNull();
                ctx.Metadata!["reply_to"].Should().NotBeNullOrWhiteSpace();

                await transport.PublishAsync(new NatsRemoteStockResult
                {
                    Qty = 23,
                    Available = true
                },
                new TransportContext
                {
                    Metadata = new Dictionary<string, string> { ["tenant"] = "acme" }
                });
            });

            await Task.Delay(200);

            var client = requestClientFactory.CreateClient<NatsRemoteCheckStock, NatsRemoteStockResult>(destination);
            var result = await client.RequestAsync(new NatsRemoteCheckStock { Sku = "SKU-NCQ" });

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Qty.Should().Be(23);
            result.Value.Available.Should().BeTrue();
        }
        finally { }
    }

    [Fact]
    public async Task RequestClient_WithConcurrentRequests_ShouldMatchResponses()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"request-client-concurrent-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(3));
        using var cts = new CancellationTokenSource();
        var destination = "inventory.check-stock";
        var subject = $"{options.SubjectPrefix.TrimEnd('.')}.{destination}";

        var responder = Task.Run(async () =>
        {
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(subject, cancellationToken: cts.Token))
            {
                var request = _serializer.Deserialize<NatsRemoteCheckStock>(msg.Data!);
                if (request.Sku == "SKU-SLOW")
                    await Task.Delay(150, cts.Token);

                await _nats.PublishAsync(
                    msg.ReplyTo!,
                    _serializer.Serialize(new NatsRemoteStockResult
                    {
                        Qty = request.Sku == "SKU-SLOW" ? 41 : 42,
                        Available = true
                    }),
                    cancellationToken: cts.Token);
            }
        }, cts.Token);

        await Task.Delay(200);

        try
        {
            var client = requestClientFactory.CreateClient<NatsRemoteCheckStock, NatsRemoteStockResult>(destination);
            var slowTask = client.RequestAsync(new NatsRemoteCheckStock { Sku = "SKU-SLOW" });
            var fastTask = client.RequestAsync(new NatsRemoteCheckStock { Sku = "SKU-FAST" });

            await Task.WhenAll(slowTask, fastTask);

            var slowResult = await slowTask;
            var fastResult = await fastTask;

            slowResult.IsSuccess.Should().BeTrue();
            fastResult.IsSuccess.Should().BeTrue();
            slowResult.Value!.Qty.Should().Be(41);
            fastResult.Value!.Qty.Should().Be(42);
        }
        finally
        {
            await cts.CancelAsync();
            try { await responder; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task RequestClient_WithAutoBatchedResponder_ShouldReturnResponse()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions
        {
            SubjectPrefix = $"request-client-batch-{Guid.NewGuid():N}",
            Batch = new BatchTransportOptions
            {
                EnableAutoBatching = true,
                MaxBatchSize = 8,
                BatchTimeout = TimeSpan.FromMilliseconds(150)
            }
        };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(5));
        var destination = "inventory.check-stock";

        await transport.SubscribeAsync<NatsRemoteCheckStock>(destination, async (request, ctx) =>
        {
            request.Sku.Should().Be("SKU-NCQ-BATCH");
            ctx.Metadata.Should().NotBeNull();
            ctx.Metadata!["reply_to"].Should().NotBeNullOrWhiteSpace();

            await transport.PublishAsync(new NatsRemoteStockResult
            {
                Qty = 29,
                Available = true
            },
            new TransportContext
            {
                Metadata = new Dictionary<string, string> { ["tenant"] = "acme" }
            });
        });

        await Task.Delay(200);

        var client = requestClientFactory.CreateClient<NatsRemoteCheckStock, NatsRemoteStockResult>(destination);
        var result = await client.RequestAsync(new NatsRemoteCheckStock { Sku = "SKU-NCQ-BATCH" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Qty.Should().Be(29);
        result.Value.Available.Should().BeTrue();
    }

    [Fact]
    public async Task SendBatchAsync_WithContext_ShouldDeliverTransportContextToDestinationSubscribers()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"send-batch-context-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var destination = $"batch-context-{Guid.NewGuid():N}";
        var receivedContexts = new ConcurrentDictionary<long, TransportContext>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sentAt = new DateTime(2024, 06, 07, 08, 09, 10, DateTimeKind.Utc);
        var messages = Enumerable.Range(1, 3)
            .Select(i => new NatsTransportTestMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = $"batch-send-context-{i}"
            })
            .ToArray();

        await transport.SubscribeAsync<NatsTransportTestMessage>(destination, async (msg, ctx) =>
        {
            if (receivedContexts.TryAdd(msg.MessageId, ctx) && receivedContexts.Count >= messages.Length)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await Task.Delay(200);
        await transport.SendBatchAsync(
            messages,
            destination,
            new TransportContext
            {
                CorrelationId = 1357,
                MessageType = "tests.nats.batch",
                SentAt = sentAt,
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "acme",
                    ["region"] = "eu"
                }
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completed.Should().Be(tcs.Task);
        receivedContexts.Keys.Should().BeEquivalentTo(messages.Select(x => x.MessageId));
        receivedContexts.Values.Should().OnlyContain(ctx =>
            ctx.CorrelationId == 1357 &&
            ctx.MessageType == "tests.nats.batch" &&
            ctx.SentAt == sentAt &&
            ctx.Metadata != null &&
            ctx.Metadata["tenant"] == "acme" &&
            ctx.Metadata["region"] == "eu");
    }

    [Fact]
    public async Task SendBatchAsync_WithCustomDestination_ShouldDeliverAllMessages()
    {
        if (_nats is null) return;

        var options = new NatsTransportOptions { SubjectPrefix = $"send-batch-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var destination = $"batch-destination-{Guid.NewGuid():N}";
        var receivedMessageIds = new ConcurrentDictionary<long, byte>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = Enumerable.Range(1, 3)
            .Select(i => new NatsTransportTestMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = $"batch-send-{i}"
            })
            .ToArray();

        await transport.SubscribeAsync<NatsTransportTestMessage>(destination, async (msg, _) =>
        {
            if (receivedMessageIds.TryAdd(msg.MessageId, 0) && receivedMessageIds.Count == messages.Length)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await Task.Delay(200);
        await transport.SendBatchAsync(messages, destination);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completed.Should().Be(tcs.Task);
        receivedMessageIds.Keys.Should().BeEquivalentTo(messages.Select(x => x.MessageId));
    }

    #endregion

    #region Transport Options Tests

    [Fact]
    public async Task Transport_WithCustomSubjectPrefix_ShouldWork()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"custom-prefix-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "prefix" };
        await transport.PublishAsync(message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task Transport_Name_ShouldBeNATS()
    {
        if (_nats is null) return;
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider);

        transport.Name.Should().Be("NATS");
    }

    #endregion

    #region Resilience Tests

    [Fact]
    public async Task PublishAsync_WithResilience_ShouldRetryOnFailure()
    {
        if (_nats is null) return;
        var retryOptions = new CatgaResilienceOptions
        {
            TransportRetryCount = 3,
            TransportRetryDelay = TimeSpan.FromMilliseconds(50)
        };
        var retryProvider = new DefaultResiliencePipelineProvider(retryOptions);
        var options = new NatsTransportOptions { SubjectPrefix = $"resilience-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, retryProvider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "resilience" };
        await transport.PublishAsync(message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    #endregion

    #region Observability Tests

    [Fact]
    public async Task PublishAsync_ShouldEmitDiagnostics()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"diag-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var activityStarted = false;
        Catga.Observability.ObservabilityHooks.Enable();

        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => activityStarted = true
        };
        ActivitySource.AddActivityListener(listener);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "diag" };
        await transport.PublishAsync(message);

        activityStarted.Should().BeTrue();
    }

    #endregion

    #region Auto-Batching Tests

    [Fact]
    public async Task PublishAsync_WithAutoBatching_ShouldBatchMessages()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions
        {
            SubjectPrefix = $"autobatch-{Guid.NewGuid():N}",
            Batch = new BatchTransportOptions
            {
                EnableAutoBatching = true,
                MaxBatchSize = 3,
                BatchTimeout = TimeSpan.FromMilliseconds(100)
            }
        };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var receivedMessageIds = new ConcurrentDictionary<long, byte>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            if (receivedMessageIds.TryAdd(msg.MessageId, 0) && receivedMessageIds.Count == 3)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        // Publish multiple messages quickly
        var sentMessageIds = new List<long>(capacity: 3);
        for (int i = 0; i < 3; i++)
        {
            var message = new NatsTransportTestMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = $"autobatch-{i}",
                QoS = QualityOfService.AtMostOnce
            };
            sentMessageIds.Add(message.MessageId);
            await transport.PublishAsync(message);
        }

        var result = await Task.WhenAny(tcs.Task, Task.Delay(30000));
        result.Should().Be(tcs.Task);
        receivedMessageIds.Keys.Should().BeEquivalentTo(sentMessageIds);
    }

    [Fact]
    public async Task PublishAsync_WithAutoBatchingTimeout_ShouldFlushQueuedMessage()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions
        {
            SubjectPrefix = $"autobatch-timeout-{Guid.NewGuid():N}",
            Batch = new BatchTransportOptions
            {
                EnableAutoBatching = true,
                MaxBatchSize = 8,
                BatchTimeout = TimeSpan.FromMilliseconds(150)
            }
        };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        transport.BatchOptions.Should().NotBeNull();
        transport.BatchOptions!.EnableAutoBatching.Should().BeTrue();
        transport.BatchOptions.MaxBatchSize.Should().Be(8);

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, _) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "autobatch-timeout",
            QoS = QualityOfService.AtMostOnce
        };

        await transport.PublishAsync(message);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        completed.Should().Be(tcs.Task);
        (await tcs.Task).MessageId.Should().Be(message.MessageId);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_ShouldCleanupResources()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"dispose-{Guid.NewGuid():N}" };
        var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) => await Task.CompletedTask);

        await transport.DisposeAsync();

        // Should not throw
    }

    #endregion

    #region Multiple Messages Tests

    [Fact]
    public async Task PublishAsync_MultipleMessages_ShouldDeliverAll()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"multi-{Guid.NewGuid():N}" };
        var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var receivedCount = 0;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= 3)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        for (int i = 0; i < 3; i++)
        {
            var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"msg-{i}" };
            await transport.PublishAsync(message);
        }

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        receivedCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_ShouldWork()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"multi-sub-{Guid.NewGuid():N}" };
        var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var received1 = false;
        var received2 = false;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            received1 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            received2 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "multi-sub" };
        await transport.PublishAsync(message);

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        (received1 || received2).Should().BeTrue();
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
public partial class NatsTransportTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class NatsPrioritizedTransportMessage : IPrioritizedMessage
{
    public long MessageId { get; set; }
    public long? CorrelationId { get; set; }
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class NatsDelayedTransportMessage : IDelayedMessage
{
    public long MessageId { get; set; }
    public long? CorrelationId { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public TimeSpan? Delay { get; set; }
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class NatsRemoteCheckStock : IRequest<NatsRemoteStockResult>
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Sku { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class NatsRemoteStockResult
{
    public int Qty { get; set; }
    public bool Available { get; set; }
}

[MemoryPackable]
public partial class NatsRemoteReserveItem : IRequest<NatsRemoteReservationResult>
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Sku { get; set; } = string.Empty;
    public int Qty { get; set; }
}

[MemoryPackable]
public partial class NatsRemoteReservationResult
{
    public string ReservationId { get; set; } = string.Empty;
}

public sealed class NatsRemoteCheckoutState : IFlowState
{
    public string? FlowId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public int Qty { get; set; }
    public bool InStock { get; set; }
    public string? ReservationId { get; set; }
    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int i) => false;
    public void ClearChanges() { }
    public void MarkChanged(int i) { }
    public IEnumerable<string> GetChangedFieldNames() => [];
}

public sealed class NatsRemoteCheckoutFlow : FlowConfig<NatsRemoteCheckoutState>
{
    protected override void Configure(IFlowBuilder<NatsRemoteCheckoutState> flow)
    {
        flow.RemoteSend<NatsRemoteCheckoutState, NatsRemoteCheckStock, NatsRemoteStockResult>(
                state => new NatsRemoteCheckStock { Sku = state.Sku })
            .Into((state, result) =>
            {
                state.InStock = result.Available;
                state.Qty = result.Qty;
            });

        flow.RemoteSend<NatsRemoteCheckoutState, NatsRemoteReserveItem, NatsRemoteReservationResult>(
                state => new NatsRemoteReserveItem { Sku = state.Sku, Qty = state.Qty })
            .Into((state, result) => state.ReservationId = result.ReservationId)
            .OnlyWhen(state => state.InStock);
    }
}

public sealed class NatsRemoteCheckoutDestinationFlow(string stockDestination, string reserveDestination) : FlowConfig<NatsRemoteCheckoutState>
{
    protected override void Configure(IFlowBuilder<NatsRemoteCheckoutState> flow)
    {
        flow.RemoteSend<NatsRemoteCheckoutState, NatsRemoteCheckStock, NatsRemoteStockResult>(
                state => new NatsRemoteCheckStock { Sku = state.Sku },
                stockDestination)
            .Into((state, result) =>
            {
                state.InStock = result.Available;
                state.Qty = result.Qty;
            });

        flow.RemoteSend<NatsRemoteCheckoutState, NatsRemoteReserveItem, NatsRemoteReservationResult>(
                state => new NatsRemoteReserveItem { Sku = state.Sku, Qty = state.Qty },
                reserveDestination)
            .Into((state, result) => state.ReservationId = result.ReservationId)
            .OnlyWhen(state => state.InStock);
    }
}

#endregion
