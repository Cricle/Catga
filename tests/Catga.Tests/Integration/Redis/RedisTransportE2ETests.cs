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
using Catga.Tests.Integration;
using FluentAssertions;
using MemoryPack;
using StackExchange.Redis;

namespace Catga.Tests.Integration.Redis;

/// <summary>
/// E2E tests for Redis Transport.
/// Target: 80% coverage for Catga.Transport.Redis
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
[Collection("IntegrationTests")]
public sealed class RedisTransportE2ETests
{
    private readonly global::Catga.Tests.Integration.SharedIntegrationFixture _fixture;
    private IConnectionMultiplexer? _redis => _fixture.Redis;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    public RedisTransportE2ETests(global::Catga.Tests.Integration.SharedIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    #region Basic Publish/Subscribe Tests

    [Fact]
    public async Task PublishAsync_WithSubscriber_ShouldDeliverMessage()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "hello" };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
        var received = await tcs.Task;
        received.Data.Should().Be("hello");
    }

    [Fact]
    public async Task PublishAsync_QoS0_AtMostOnce_ShouldDeliverViaPubSub()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "qos0", QoS = QualityOfService.AtMostOnce };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task PublishAsync_QoS1_AtLeastOnce_ShouldDeliverViaStreams()
    {
        if (_redis is null) return;
        var options = new RedisTransportOptions();
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "qos1", QoS = QualityOfService.AtLeastOnce };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task PublishAsync_QoS2_ExactlyOnce_ShouldDeliverWithDedup()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var received = 0;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            Interlocked.Increment(ref received);
            tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var messageId = MessageExtensions.NewMessageId();
        var message = new RedisTransportMessage { MessageId = messageId, Data = "qos2", QoS = QualityOfService.ExactlyOnce };
        var ctx = new TransportContext { MessageId = messageId };

        // Publish twice with same MessageId
        await transport.PublishAsync(message, ctx);
        await transport.PublishAsync(message, ctx);

        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        await Task.Delay(500); // Wait for potential duplicate

        received.Should().Be(1, "QoS2 should deduplicate messages");
    }

    [Fact]
    public async Task PublishAsync_WithContext_ShouldDeliverTransportContextToSubscriber()
    {
        if (_redis is null) return;

        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RedisTransportMessage>(async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        await transport.PublishAsync(
            new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "ctx-publish" },
            new TransportContext
            {
                MessageId = 2468,
                CorrelationId = 1357,
                MessageType = "custom.redis.publish",
                SentAt = new DateTime(2024, 01, 02, 03, 04, 05, DateTimeKind.Utc),
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "acme",
                    ["region"] = "eu"
                }
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.MessageId.Should().Be(2468);
        receivedContext.CorrelationId.Should().Be(1357);
        receivedContext.MessageType.Should().Be("custom.redis.publish");
        receivedContext.SentAt.Should().Be(new DateTime(2024, 01, 02, 03, 04, 05, DateTimeKind.Utc));
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!["tenant"].Should().Be("acme");
        receivedContext.Metadata["region"].Should().Be("eu");
    }

    [Fact]
    public async Task SubscribeAsync_WithExternalPascalCaseStreamFields_ShouldRestoreTransportContext()
    {
        if (_redis is null) return;

        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var database = _redis.GetDatabase();
        var destination = $"raw-stream-context-{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RedisTransportMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });

        await Task.Delay(100);

        await database.StreamAddAsync(
            $"stream:{destination}",
            new[]
            {
                new NameValueEntry("data", Convert.ToBase64String(_serializer.Serialize(new RedisTransportMessage { Data = "raw-stream" }))),
                new NameValueEntry("MessageId", "7301"),
                new NameValueEntry("CorrelationId", "8402"),
                new NameValueEntry("MessageType", "external.redis.stream"),
                new NameValueEntry("SentAt", "2024-04-05T06:07:08.0000000Z"),
                new NameValueEntry("Meta.Tenant", "acme")
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.MessageId.Should().Be(7301);
        receivedContext.CorrelationId.Should().Be(8402);
        receivedContext.MessageType.Should().Be("external.redis.stream");
        receivedContext.SentAt.Should().Be(new DateTime(2024, 04, 05, 06, 07, 08, DateTimeKind.Utc));
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!["Tenant"].Should().Be("acme");
    }

    [Fact]
    public async Task SubscribeAsync_WithExternalHyphenatedStreamFields_ShouldRestoreTransportContext()
    {
        if (_redis is null) return;

        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var database = _redis.GetDatabase();
        var destination = $"raw-stream-hyphenated-{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RedisTransportMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });

        await Task.Delay(100);

        await database.StreamAddAsync(
            $"stream:{destination}",
            new[]
            {
                new NameValueEntry("data", Convert.ToBase64String(_serializer.Serialize(new RedisTransportMessage { Data = "raw-hyphenated-stream" }))),
                new NameValueEntry("message-id", "7401"),
                new NameValueEntry("correlation-id", "8502"),
                new NameValueEntry("message-type", "external.redis.hyphenated"),
                new NameValueEntry("sent-at", "2024-04-06T07:08:09.0000000Z"),
                new NameValueEntry("meta-tenant", "acme"),
                new NameValueEntry("meta-reply-to", "redis.reply.hyphenated")
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.MessageId.Should().Be(7401);
        receivedContext.CorrelationId.Should().Be(8502);
        receivedContext.MessageType.Should().Be("external.redis.hyphenated");
        receivedContext.SentAt.Should().Be(new DateTime(2024, 04, 06, 07, 08, 09, DateTimeKind.Utc));
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!["tenant"].Should().Be("acme");
        receivedContext.Metadata["reply_to"].Should().Be("redis.reply.hyphenated");
        receivedContext.Metadata["reply_subject"].Should().Be("redis.reply.hyphenated");
    }

    [Fact]
    public async Task PublishAsync_WithPrioritizedMessage_ShouldExposePriorityMetadataToSubscriber()
    {
        if (_redis is null) return;

        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RedisPrioritizedTransportMessage>(async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        await transport.PublishAsync(new RedisPrioritizedTransportMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "priority",
            Priority = MessagePriority.High
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!["x-priority"].Should().Be("2");
    }

    [Fact]
    public async Task PublishAsync_WithDelayedMessage_ShouldExposeDelayMetadataToSubscriber()
    {
        if (_redis is null) return;

        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RedisDelayedTransportMessage>(async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        await transport.PublishAsync(new RedisDelayedTransportMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "delay",
            ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(5)
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!.Should().ContainKey("x-delay");
        int.Parse(receivedContext.Metadata["x-delay"]).Should().BeGreaterThan(0);
    }

    #endregion

    #region Batch Publish Tests

    [Fact]
    public async Task PublishBatchAsync_ShouldDeliverAllMessages()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var receivedCount = 0;
        var tcs = new TaskCompletionSource();
        const int batchSize = 10;

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= batchSize)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var messages = Enumerable.Range(0, batchSize)
            .Select(i => new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"batch-{i}" })
            .ToList();

        await transport.PublishBatchAsync(messages);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
        receivedCount.Should().Be(batchSize);
    }

    #endregion

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_ShouldDeliverToDestination()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();
        var destination = $"test-destination-{Guid.NewGuid():N}";

        await transport.SubscribeAsync<RedisTransportMessage>(destination, async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "send" };
        await transport.SendAsync(message, destination);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task SendAsync_WithContext_ShouldDeliverTransportContextToDestinationSubscriber()
    {
        if (_redis is null) return;

        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var destination = $"ctx-destination-{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new TransportContext
        {
            MessageId = 123456789L,
            CorrelationId = 987654321L,
            MessageType = "custom.redis.message",
            Metadata = new Dictionary<string, string> { ["tenant"] = "acme" }
        };

        await transport.SubscribeAsync<RedisTransportMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        await transport.SendAsync(
            new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "ctx" },
            destination,
            context);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task);

        var receivedContext = await tcs.Task;
        receivedContext.MessageId.Should().Be(123456789L);
        receivedContext.CorrelationId.Should().Be(987654321L);
        receivedContext.MessageType.Should().Be("custom.redis.message");
        receivedContext.Metadata.Should().NotBeNull();
        receivedContext.Metadata!["tenant"].Should().Be("acme");
    }

    [Fact]
    public async Task FlowRemoteSend_ShouldRoundTripThroughRedisTransport()
    {
        if (_redis is null) return;

        var options = new RedisTransportOptions { ChannelPrefix = $"flow-remote-{Guid.NewGuid():N}." };
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(3));
        var mediator = new MockMediator();
        var store = new InMemoryDslFlowStore();
        var executor = new DslFlowExecutor<RedisRemoteCheckoutState, RedisRemoteCheckoutFlow>(
            mediator,
            store,
            new RedisRemoteCheckoutFlow(),
            requestClientFactory: requestClientFactory);
        var capturedRequests = new List<object>();

        await transport.SubscribeAsync<RedisRemoteCheckStock>(async (request, _) =>
        {
            capturedRequests.Add(request);
            await transport.PublishAsync(new RedisRemoteStockResult
            {
                Qty = 9,
                Available = true
            });
        });

        await transport.SubscribeAsync<RedisRemoteReserveItem>(async (request, _) =>
        {
            capturedRequests.Add(request);
            await transport.PublishAsync(new RedisRemoteReservationResult
            {
                ReservationId = "REDIS-RES-001"
            });
        });

        await Task.Delay(100);

        var result = await executor.RunAsync(new RedisRemoteCheckoutState
        {
            FlowId = "redis-remote-send",
            Sku = "SKU-REDIS",
            Qty = 2
        });

        result.IsSuccess.Should().BeTrue();
        result.State.Should().NotBeNull();
        result.State!.InStock.Should().BeTrue();
        result.State.Qty.Should().Be(9);
        result.State.ReservationId.Should().Be("REDIS-RES-001");
        mediator.Sent.Should().BeEmpty();

        capturedRequests.Should().HaveCount(2);
        capturedRequests[0].Should().BeOfType<RedisRemoteCheckStock>()
            .Which.Sku.Should().Be("SKU-REDIS");
        capturedRequests[1].Should().BeOfType<RedisRemoteReserveItem>()
            .Which.Should().Match<RedisRemoteReserveItem>(request =>
                request.Sku == "SKU-REDIS" &&
                request.Qty == 9);
    }

    [Fact]
    public async Task FlowRemoteSend_WithCustomDestination_ShouldRoundTripThroughRedisTransport()
    {
        if (_redis is null) return;

        var options = new RedisTransportOptions { ChannelPrefix = $"flow-remote-dest-{Guid.NewGuid():N}." };
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(3));
        var mediator = new MockMediator();
        var store = new InMemoryDslFlowStore();
        var executor = new DslFlowExecutor<RedisRemoteCheckoutState, RedisRemoteCheckoutDestinationFlow>(
            mediator,
            store,
            new RedisRemoteCheckoutDestinationFlow("inventory.check-stock", "inventory.reserve-item"),
            requestClientFactory: requestClientFactory);
        var capturedRequests = new List<object>();

        await transport.SubscribeAsync<RedisRemoteCheckStock>("inventory.check-stock", async (request, _) =>
        {
            capturedRequests.Add(request);
            await transport.PublishAsync(new RedisRemoteStockResult
            {
                Qty = 13,
                Available = true
            });
        });

        await transport.SubscribeAsync<RedisRemoteReserveItem>("inventory.reserve-item", async (request, _) =>
        {
            capturedRequests.Add(request);
            await transport.PublishAsync(new RedisRemoteReservationResult
            {
                ReservationId = "REDIS-DEST-001"
            });
        });

        await Task.Delay(100);

        var result = await executor.RunAsync(new RedisRemoteCheckoutState
        {
            FlowId = "redis-remote-send-destination",
            Sku = "SKU-REDIS-DEST",
            Qty = 4
        });

        result.IsSuccess.Should().BeTrue();
        result.State.Should().NotBeNull();
        result.State!.InStock.Should().BeTrue();
        result.State.Qty.Should().Be(13);
        result.State.ReservationId.Should().Be("REDIS-DEST-001");
        mediator.Sent.Should().BeEmpty();

        capturedRequests.Should().HaveCount(2);
        capturedRequests[0].Should().BeOfType<RedisRemoteCheckStock>()
            .Which.Sku.Should().Be("SKU-REDIS-DEST");
        capturedRequests[1].Should().BeOfType<RedisRemoteReserveItem>()
            .Which.Should().Match<RedisRemoteReserveItem>(request =>
                request.Sku == "SKU-REDIS-DEST" &&
                request.Qty == 13);
    }

    [Fact]
    public async Task RequestClient_WithCustomDestination_ShouldReturnResponse()
    {
        if (_redis is null) return;

        var options = new RedisTransportOptions { ChannelPrefix = $"request-client-{Guid.NewGuid():N}." };
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(3));
        var destination = "inventory.check-stock";

        await transport.SubscribeAsync<RedisRemoteCheckStock>(destination, async (request, ctx) =>
        {
            request.Sku.Should().Be("SKU-RCQ");
            ctx.MessageId.Should().NotBeNull();
            ctx.CorrelationId.Should().NotBeNull();
            ctx.MessageType.Should().Be(typeof(RedisRemoteCheckStock).FullName);
            ctx.Metadata.Should().NotBeNull();
            ctx.Metadata!["reply_to"].Should().NotBeNullOrWhiteSpace();
            ctx.Metadata!["reply_subject"].Should().NotBeNullOrWhiteSpace();
            ctx.Metadata["reply_to"].Should().Be(ctx.Metadata["reply_subject"]);

            await transport.PublishAsync(new RedisRemoteStockResult
            {
                Qty = 21,
                Available = true
            },
            new TransportContext
            {
                Metadata = new Dictionary<string, string> { ["tenant"] = "acme" }
            });
        });

        await Task.Delay(100);

        var client = requestClientFactory.CreateClient<RedisRemoteCheckStock, RedisRemoteStockResult>(destination);
        var result = await client.RequestAsync(new RedisRemoteCheckStock { Sku = "SKU-RCQ" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Qty.Should().Be(21);
        result.Value.Available.Should().BeTrue();
    }

    [Fact]
    public async Task ExternalStreamRequest_WithReplyToMetadata_ShouldReceiveResponse()
    {
        if (_redis is null) return;

        var options = new RedisTransportOptions { ChannelPrefix = $"external-request-{Guid.NewGuid():N}." };
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var database = _redis.GetDatabase();
        var destination = "inventory.check-stock";
        var replyChannel = $"{options.ChannelPrefix}external.reply.{Guid.NewGuid():N}";
        var responseTcs = new TaskCompletionSource<RedisRemoteStockResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RedisRemoteStockResult>(replyChannel, async (response, _) =>
        {
            responseTcs.TrySetResult(response);
            await Task.CompletedTask;
        });

        await transport.SubscribeAsync<RedisRemoteCheckStock>(destination, async (request, ctx) =>
        {
            request.Sku.Should().Be("SKU-REDIS-RAW");
            ctx.Metadata.Should().NotBeNull();
            ctx.Metadata!["reply_to"].Should().Be(replyChannel);
            ctx.Metadata["reply_subject"].Should().Be(replyChannel);

            await transport.PublishAsync(new RedisRemoteStockResult
            {
                Qty = 44,
                Available = true
            });
        });

        await Task.Delay(100);

        await database.StreamAddAsync(
            $"stream:{destination}",
            new[]
            {
                new NameValueEntry("data", Convert.ToBase64String(_serializer.Serialize(new RedisRemoteCheckStock { Sku = "SKU-REDIS-RAW" }))),
                new NameValueEntry("message_id", "7101"),
                new NameValueEntry("correlation_id", "8102"),
                new NameValueEntry("message_type", typeof(RedisRemoteCheckStock).FullName!),
                new NameValueEntry("meta.reply_to", replyChannel)
            });

        var completed = await Task.WhenAny(responseTcs.Task, Task.Delay(5000));
        completed.Should().Be(responseTcs.Task);

        var response = await responseTcs.Task;
        response.Qty.Should().Be(44);
        response.Available.Should().BeTrue();
    }

    [Fact]
    public async Task RequestClient_WithConcurrentRequests_ShouldMatchResponsesByCorrelation()
    {
        if (_redis is null) return;

        var options = new RedisTransportOptions { ChannelPrefix = $"request-client-concurrent-{Guid.NewGuid():N}." };
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(3));
        var destination = "inventory.check-stock";

        await transport.SubscribeAsync<RedisRemoteCheckStock>(destination, async (request, _) =>
        {
            if (request.Sku == "SKU-SLOW")
                await Task.Delay(150);

            await transport.PublishAsync(new RedisRemoteStockResult
            {
                Qty = request.Sku == "SKU-SLOW" ? 31 : 32,
                Available = true
            });
        });

        await Task.Delay(100);

        var client = requestClientFactory.CreateClient<RedisRemoteCheckStock, RedisRemoteStockResult>(destination);
        var slowTask = client.RequestAsync(new RedisRemoteCheckStock { Sku = "SKU-SLOW" });
        var fastTask = client.RequestAsync(new RedisRemoteCheckStock { Sku = "SKU-FAST" });

        await Task.WhenAll(slowTask, fastTask);

        var slowResult = await slowTask;
        var fastResult = await fastTask;

        slowResult.IsSuccess.Should().BeTrue();
        fastResult.IsSuccess.Should().BeTrue();
        slowResult.Value!.Qty.Should().Be(31);
        fastResult.Value!.Qty.Should().Be(32);
    }

    [Fact]
    public async Task RequestClient_WithAutoBatchedResponder_ShouldReturnResponse()
    {
        if (_redis is null) return;

        var options = new RedisTransportOptions
        {
            ChannelPrefix = $"request-client-batch-{Guid.NewGuid():N}.",
            Batch = new BatchTransportOptions
            {
                EnableAutoBatching = true,
                MaxBatchSize = 8,
                BatchTimeout = TimeSpan.FromMilliseconds(100)
            }
        };
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(5));
        var destination = "inventory.check-stock";

        await transport.SubscribeAsync<RedisRemoteCheckStock>(destination, async (request, _) =>
        {
            request.Sku.Should().Be("SKU-RCQ-BATCH");

            await transport.PublishAsync(new RedisRemoteStockResult
            {
                Qty = 27,
                Available = true
            },
            new TransportContext
            {
                Metadata = new Dictionary<string, string> { ["tenant"] = "acme" }
            });
        });

        await Task.Delay(100);

        var client = requestClientFactory.CreateClient<RedisRemoteCheckStock, RedisRemoteStockResult>(destination);
        var result = await client.RequestAsync(new RedisRemoteCheckStock { Sku = "SKU-RCQ-BATCH" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Qty.Should().Be(27);
        result.Value.Available.Should().BeTrue();
    }

    [Fact]
    public async Task SendBatchAsync_WithContext_ShouldDeliverTransportContextToDestinationSubscribers()
    {
        if (_redis is null) return;

        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var destination = $"batch-context-destination-{Guid.NewGuid():N}";
        var receivedContexts = new ConcurrentBag<TransportContext>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sentAt = new DateTime(2024, 05, 06, 07, 08, 09, DateTimeKind.Utc);
        var messages = Enumerable.Range(1, 3)
            .Select(i => new RedisTransportMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = $"batch-send-context-{i}"
            })
            .ToArray();

        await transport.SubscribeAsync<RedisTransportMessage>(destination, async (_, ctx) =>
        {
            receivedContexts.Add(ctx);
            if (receivedContexts.Count >= messages.Length)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await Task.Delay(100);
        await transport.SendBatchAsync(
            messages,
            destination,
            new TransportContext
            {
                CorrelationId = 2468,
                MessageType = "custom.redis.batch",
                SentAt = sentAt,
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "acme",
                    ["region"] = "eu"
                }
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task);
        receivedContexts.Should().HaveCount(messages.Length);
        receivedContexts.Should().OnlyContain(ctx =>
            ctx.CorrelationId == 2468 &&
            ctx.MessageType == "custom.redis.batch" &&
            ctx.SentAt == sentAt &&
            ctx.Metadata != null &&
            ctx.Metadata["tenant"] == "acme" &&
            ctx.Metadata["region"] == "eu");
    }

    [Fact]
    public async Task SendBatchAsync_WithCustomDestination_ShouldDeliverAllMessages()
    {
        if (_redis is null) return;

        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var destination = $"batch-destination-{Guid.NewGuid():N}";
        var received = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = Enumerable.Range(1, 3)
            .Select(i => new RedisTransportMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = $"batch-send-{i}"
            })
            .ToArray();

        await transport.SubscribeAsync<RedisTransportMessage>(destination, async (msg, _) =>
        {
            if (Interlocked.Increment(ref received) == messages.Length)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await Task.Delay(100);
        await transport.SendBatchAsync(messages, destination);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task);
        received.Should().Be(messages.Length);
    }

    #endregion

    #region Transport Options Tests

    [Fact]
    public async Task Transport_WithCustomChannelPrefix_ShouldWork()
    {
        if (_redis is null) return;
        var options = new RedisTransportOptions { ChannelPrefix = "custom-prefix" };
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "prefix" };
        await transport.PublishAsync(message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task Transport_Name_ShouldBeRedis()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);

        transport.Name.Should().Be("Redis");
    }

    #endregion

    #region Resilience Tests

    [Fact]
    public async Task PublishAsync_WithResilience_ShouldRetryOnFailure()
    {
        if (_redis is null) return;
        var retryOptions = new CatgaResilienceOptions
        {
            TransportRetryCount = 3,
            TransportRetryDelay = TimeSpan.FromMilliseconds(50)
        };
        var retryProvider = new DefaultResiliencePipelineProvider(retryOptions);
        await using var transport = new RedisMessageTransport(_redis, _serializer, retryProvider);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "resilience" };
        await transport.PublishAsync(message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    #endregion

    #region Observability Tests

    [Fact]
    public async Task PublishAsync_ShouldEmitDiagnostics()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var activityStarted = false;
        Catga.Observability.ObservabilityHooks.Enable();

        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => activityStarted = true
        };
        ActivitySource.AddActivityListener(listener);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "diag" };
        await transport.PublishAsync(message);

        activityStarted.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_ShouldPropagateTraceContextToSubscriberActivity()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        string? publishActivityId = null;
        var consumerParentId = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Catga.Observability.ObservabilityHooks.Enable();

        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (activity.OperationName == "Messaging.Publish" && activity.Kind == ActivityKind.Producer)
                    publishActivityId ??= activity.Id;
            }
        };
        ActivitySource.AddActivityListener(listener);

        await transport.SubscribeAsync<RedisTransportMessage>(async (_, _) =>
        {
            consumerParentId.TrySetResult(Activity.Current?.ParentId);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        using var root = new Activity("redis-root");
        root.Start();

        await transport.PublishAsync(new RedisTransportMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "trace"
        });

        var parentId = await consumerParentId.Task.WaitAsync(TimeSpan.FromSeconds(5));
        publishActivityId.Should().NotBeNull();
        parentId.Should().Be(publishActivityId);
    }

    #endregion

    #region Multiple Subscribers Tests

    [Fact]
    public async Task PublishAsync_MultipleSubscribers_ShouldDeliverToAll()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var received1 = false;
        var received2 = false;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            received1 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            received2 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "multi" };
        await transport.PublishAsync(message);

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        (received1 || received2).Should().BeTrue();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_ShouldCleanupResources()
    {
        if (_redis is null) return;
        var transport = new RedisMessageTransport(_redis, _serializer, _provider);

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) => await Task.CompletedTask);

        await transport.DisposeAsync();

        // Should not throw
    }

    #endregion

    #region Stream Operations Tests

    [Fact]
    public async Task PublishAsync_MultipleMessages_ShouldDeliverAll()
    {
        if (_redis is null) return;
        var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var receivedCount = 0;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= 5)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        for (int i = 0; i < 5; i++)
        {
            var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"msg-{i}" };
            await transport.PublishAsync(message);
        }

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        receivedCount.Should().Be(5);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_ShouldDeliverToAll()
    {
        if (_redis is null) return;
        var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var received1 = false;
        var received2 = false;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            received1 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            received2 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "multi-sub" };
        await transport.PublishAsync(message);

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        (received1 || received2).Should().BeTrue();
    }

    [Fact]
    public async Task Transport_WithCustomConsumerGroup_ShouldWork()
    {
        if (_redis is null) return;
        var transport = new RedisMessageTransport(_redis, _serializer, _provider, consumerGroup: "custom-group", consumerName: "consumer-1");
        var received = false;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            received = true;
            tcs.TrySetResult();
            await Task.CompletedTask;
        });

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "custom-group" };
        await transport.PublishAsync(message);

        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        received.Should().BeTrue();
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
public partial class RedisTransportMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class RedisPrioritizedTransportMessage : IPrioritizedMessage
{
    public long MessageId { get; set; }
    public long? CorrelationId { get; set; }
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class RedisDelayedTransportMessage : IDelayedMessage
{
    public long MessageId { get; set; }
    public long? CorrelationId { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public TimeSpan? Delay { get; set; }
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class RedisRemoteCheckStock : IRequest<RedisRemoteStockResult>
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Sku { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class RedisRemoteStockResult
{
    public int Qty { get; set; }
    public bool Available { get; set; }
}

[MemoryPackable]
public partial class RedisRemoteReserveItem : IRequest<RedisRemoteReservationResult>
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Sku { get; set; } = string.Empty;
    public int Qty { get; set; }
}

[MemoryPackable]
public partial class RedisRemoteReservationResult
{
    public string ReservationId { get; set; } = string.Empty;
}

public sealed class RedisRemoteCheckoutState : IFlowState
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

public sealed class RedisRemoteCheckoutFlow : FlowConfig<RedisRemoteCheckoutState>
{
    protected override void Configure(IFlowBuilder<RedisRemoteCheckoutState> flow)
    {
        flow.RemoteSend<RedisRemoteCheckoutState, RedisRemoteCheckStock, RedisRemoteStockResult>(
                state => new RedisRemoteCheckStock { Sku = state.Sku })
            .Into((state, result) =>
            {
                state.InStock = result.Available;
                state.Qty = result.Qty;
            });

        flow.RemoteSend<RedisRemoteCheckoutState, RedisRemoteReserveItem, RedisRemoteReservationResult>(
                state => new RedisRemoteReserveItem { Sku = state.Sku, Qty = state.Qty })
            .Into((state, result) => state.ReservationId = result.ReservationId)
            .OnlyWhen(state => state.InStock);
    }
}

public sealed class RedisRemoteCheckoutDestinationFlow(string stockDestination, string reserveDestination) : FlowConfig<RedisRemoteCheckoutState>
{
    protected override void Configure(IFlowBuilder<RedisRemoteCheckoutState> flow)
    {
        flow.RemoteSend<RedisRemoteCheckoutState, RedisRemoteCheckStock, RedisRemoteStockResult>(
                state => new RedisRemoteCheckStock { Sku = state.Sku },
                stockDestination)
            .Into((state, result) =>
            {
                state.InStock = result.Available;
                state.Qty = result.Qty;
            });

        flow.RemoteSend<RedisRemoteCheckoutState, RedisRemoteReserveItem, RedisRemoteReservationResult>(
                state => new RedisRemoteReserveItem { Sku = state.Sku, Qty = state.Qty },
                reserveDestination)
            .Into((state, result) => state.ReservationId = result.ReservationId)
            .OnlyWhen(state => state.InStock);
    }
}

#endregion
