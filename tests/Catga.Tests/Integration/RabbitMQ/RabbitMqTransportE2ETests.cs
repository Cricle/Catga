using System.Collections.Concurrent;
using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.Messaging;
using Catga.Observability;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using Catga.Transport.RabbitMQ;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace Catga.Tests.Integration.RabbitMQ;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
[Collection("RabbitMqTransport")]
public sealed class RabbitMqTransportE2ETests : IAsyncLifetime
{
    private IContainer? _container;
    private string? _uri;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider(
        new CatgaResilienceOptions
        {
            TransportTimeout = TimeSpan.FromSeconds(30),
            TransportRetryCount = 0
        });

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning())
            return;

        var image = ResolveImage("TEST_RABBITMQ_IMAGE", "rabbitmq:3.13-alpine");
        if (image is null)
            return;
        if (!IsImageAvailableLocally(image))
            return;

        _container = new ContainerBuilder()
            .WithImage(image)
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672))
            .Build();

        await _container.StartAsync();

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(5672);
        _uri = $"amqp://guest:guest@{host}:{port}/";

        await EnsureBrokerReadyAsync(_uri);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    [SkippableFact]
    public async Task PublishAsync_WithSubscriber_ShouldDeliverMessage()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("publish");
        await using var transport = await CreateTransportAsync(options);
        var tcs = new TaskCompletionSource<RabbitMqTestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        TransportContext? receivedContext = null;

        await transport.SubscribeAsync<RabbitMqTestMessage>(async (msg, ctx) =>
        {
            receivedContext = ctx;
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        var message = new RabbitMqTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            CorrelationId = 87654321,
            Data = "rabbit-publish"
        };

        await transport.PublishAsync(message, new TransportContext
        {
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            MessageType = "rabbit.publish.context",
            SentAt = new DateTime(2024, 03, 04, 05, 06, 07, DateTimeKind.Utc),
            Metadata = new Dictionary<string, string>
            {
                ["tenant"] = "acme",
                ["region"] = "apac"
            }
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);

        var received = await tcs.Task;
        received.Data.Should().Be("rabbit-publish");
        receivedContext.Should().NotBeNull();
        receivedContext!.Value.MessageId.Should().Be(message.MessageId);
        receivedContext.Value.CorrelationId.Should().Be(message.CorrelationId);
        receivedContext.Value.MessageType.Should().Be("rabbit.publish.context");
        receivedContext.Value.SentAt.Should().Be(new DateTime(2024, 03, 04, 05, 06, 07, DateTimeKind.Utc));
        receivedContext.Value.Metadata.Should().NotBeNull();
        receivedContext.Value.Metadata!["tenant"].Should().Be("acme");
        receivedContext.Value.Metadata["region"].Should().Be("apac");
    }

    [SkippableFact]
    public async Task SubscribeAsync_WithExternalRabbitPriority_ShouldExposePriorityMetadata()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("external-priority");
        await using var transport = await CreateTransportAsync(options);
        await using var connection = await CreateConnectionAsync(_uri);
        await using var channel = await connection.CreateChannelAsync();
        var destination = "orders.external-priority";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RabbitMqTestMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        await channel.BasicPublishAsync(
            options.Exchange,
            $"{options.Prefix}{destination}",
            false,
            new BasicProperties
            {
                Priority = 3,
                MessageId = "9001",
                Type = typeof(RabbitMqTestMessage).FullName
            },
            _serializer.Serialize(new RabbitMqTestMessage
            {
                MessageId = 9001,
                Data = "native-priority"
            }),
            cancellationToken: CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);

        var received = await tcs.Task;
        received.Metadata.Should().NotBeNull();
        received.Metadata!["x-priority"].Should().Be("3");
    }

    [SkippableFact]
    public async Task SubscribeAsync_WithExternalRabbitDelayHeader_ShouldExposeDelayMetadata()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("external-delay");
        await using var transport = await CreateTransportAsync(options);
        await using var connection = await CreateConnectionAsync(_uri);
        await using var channel = await connection.CreateChannelAsync();
        var destination = "orders.external-delay";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RabbitMqTestMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        await channel.BasicPublishAsync(
            options.Exchange,
            $"{options.Prefix}{destination}",
            false,
            new BasicProperties
            {
                MessageId = "9002",
                Type = typeof(RabbitMqTestMessage).FullName,
                Headers = new Dictionary<string, object?>
                {
                    ["x-delay"] = 2500
                }
            },
            _serializer.Serialize(new RabbitMqTestMessage
            {
                MessageId = 9002,
                Data = "native-delay"
            }),
            cancellationToken: CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);

        var received = await tcs.Task;
        received.Metadata.Should().NotBeNull();
        received.Metadata!["x-delay"].Should().Be("2500");
    }

    [SkippableFact]
    public async Task SubscribeAsync_WithExternalRabbitTimestamp_ShouldExposeSentAt()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("external-timestamp");
        await using var transport = await CreateTransportAsync(options);
        await using var connection = await CreateConnectionAsync(_uri);
        await using var channel = await connection.CreateChannelAsync();
        var destination = "orders.external-timestamp";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RabbitMqTestMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        var sentAt = new DateTime(2024, 08, 09, 10, 11, 12, DateTimeKind.Utc);
        await channel.BasicPublishAsync(
            options.Exchange,
            $"{options.Prefix}{destination}",
            false,
            new BasicProperties
            {
                MessageId = "9003",
                Type = typeof(RabbitMqTestMessage).FullName,
                Timestamp = new AmqpTimestamp(new DateTimeOffset(sentAt).ToUnixTimeSeconds())
            },
            _serializer.Serialize(new RabbitMqTestMessage
            {
                MessageId = 9003,
                Data = "native-timestamp"
            }),
            cancellationToken: CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);

        var received = await tcs.Task;
        received.SentAt.Should().Be(sentAt);
    }

    [SkippableFact]
    public async Task SubscribeAsync_WithExternalMixedCaseHeaders_ShouldRestoreTransportContext()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("external-mixed-case");
        await using var transport = await CreateTransportAsync(options);
        await using var connection = await CreateConnectionAsync(_uri);
        await using var channel = await connection.CreateChannelAsync();
        var destination = "orders.external-mixed-case";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RabbitMqTestMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        await channel.BasicPublishAsync(
            options.Exchange,
            $"{options.Prefix}{destination}",
            false,
            new BasicProperties
            {
                Headers = new Dictionary<string, object?>
                {
                    ["MessageId"] = "9004",
                    ["CorrelationId"] = "12345",
                    ["MessageType"] = "external.mixed.case",
                    ["SentAt"] = "2024-04-05T06:07:08.0000000Z",
                    ["Meta-Tenant"] = "acme",
                    ["Meta-Reply-To"] = "amq.gen-hyphen",
                    ["X-Delay"] = 2500,
                    ["TraceParent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
                }
            },
            _serializer.Serialize(new RabbitMqTestMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = "mixed-case"
            }),
            cancellationToken: CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);

        var received = await tcs.Task;
        received.MessageId.Should().Be(9004);
        received.CorrelationId.Should().Be(12345);
        received.MessageType.Should().Be("external.mixed.case");
        received.SentAt.Should().Be(new DateTime(2024, 04, 05, 06, 07, 08, DateTimeKind.Utc));
        received.Metadata.Should().NotBeNull();
        received.Metadata!["Tenant"].Should().Be("acme");
        received.Metadata["reply_to"].Should().Be("amq.gen-hyphen");
        received.Metadata["reply_subject"].Should().Be("amq.gen-hyphen");
        received.Metadata["x-delay"].Should().Be("2500");
    }

    [SkippableFact]
    public async Task SendAsync_WithCustomDestination_ShouldDeliverToBoundQueue()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("send");
        await using var transport = await CreateTransportAsync(options);
        var tcs = new TaskCompletionSource<RabbitMqTestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RabbitMqTestMessage>("orders.direct", async (msg, _) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        var message = new RabbitMqTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "rabbit-send"
        };

        await transport.SendAsync(message, "orders.direct", new TransportContext { MessageId = message.MessageId });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);
        (await tcs.Task).Data.Should().Be("rabbit-send");
    }

    [SkippableFact]
    public async Task SendAsync_WithContext_ShouldRestoreTransportMetadata()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("send-context");
        await using var transport = await CreateTransportAsync(options);
        var destination = "orders.context";
        var tcs = new TaskCompletionSource<TransportContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RabbitMqTestMessage>(destination, async (_, ctx) =>
        {
            tcs.TrySetResult(ctx);
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        await transport.SendAsync(
            new RabbitMqTestMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = "rabbit-send-context"
            },
            destination,
            new TransportContext
            {
                MessageId = 11223344,
                CorrelationId = 55667788,
                MessageType = "rabbit.custom.message",
                SentAt = new DateTime(2024, 04, 05, 06, 07, 08, DateTimeKind.Utc),
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "acme",
                    ["region"] = "apac"
                }
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);

        var received = await tcs.Task;
        received.MessageId.Should().Be(11223344);
        received.CorrelationId.Should().Be(55667788);
        received.MessageType.Should().Be("rabbit.custom.message");
        received.SentAt.Should().Be(new DateTime(2024, 04, 05, 06, 07, 08, DateTimeKind.Utc));
        received.Metadata.Should().NotBeNull();
        received.Metadata!["tenant"].Should().Be("acme");
        received.Metadata["region"].Should().Be("apac");
    }

    [SkippableFact]
    public async Task PublishBatchAsync_ShouldDeliverAllMessages()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("publish-batch");
        await using var transport = await CreateTransportAsync(options);
        const int batchSize = 5;
        var received = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RabbitMqTestMessage>(async (_, _) =>
        {
            if (Interlocked.Increment(ref received) >= batchSize)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        var messages = Enumerable.Range(0, batchSize)
            .Select(i => new RabbitMqTestMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = $"rabbit-batch-{i}"
            })
            .ToArray();

        await transport.PublishBatchAsync(messages);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);
        received.Should().Be(batchSize);
    }

    [SkippableFact]
    public async Task SendBatchAsync_WithCustomDestination_ShouldDeliverAllMessages()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("send-batch");
        await using var transport = await CreateTransportAsync(options);
        const int batchSize = 5;
        var received = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var destination = "orders.batch";

        await transport.SubscribeAsync<RabbitMqTestMessage>(destination, async (_, _) =>
        {
            if (Interlocked.Increment(ref received) >= batchSize)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        var messages = Enumerable.Range(0, batchSize)
            .Select(i => new RabbitMqTestMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = $"rabbit-send-batch-{i}"
            })
            .ToArray();

        await transport.SendBatchAsync(messages, destination);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);
        received.Should().Be(batchSize);
    }

    [SkippableFact]
    public async Task SendBatchAsync_WithContext_ShouldRestoreTransportMetadataForEachMessage()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("send-batch-context");
        await using var transport = await CreateTransportAsync(options);
        const int batchSize = 3;
        var destination = "orders.batch.context";
        var receivedContexts = new ConcurrentBag<TransportContext>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sentAt = new DateTime(2024, 07, 08, 09, 10, 11, DateTimeKind.Utc);

        await transport.SubscribeAsync<RabbitMqTestMessage>(destination, async (_, ctx) =>
        {
            receivedContexts.Add(ctx);
            if (receivedContexts.Count >= batchSize)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        var messages = Enumerable.Range(0, batchSize)
            .Select(i => new RabbitMqTestMessage
            {
                MessageId = MessageExtensions.NewMessageId(),
                Data = $"rabbit-send-batch-context-{i}"
            })
            .ToArray();

        await transport.SendBatchAsync(
            messages,
            destination,
            new TransportContext
            {
                CorrelationId = 778899,
                MessageType = "rabbit.batch.context",
                SentAt = sentAt,
                Metadata = new Dictionary<string, string>
                {
                    ["tenant"] = "acme",
                    ["region"] = "apac"
                }
            });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);
        receivedContexts.Should().HaveCount(batchSize);
        receivedContexts.Should().OnlyContain(ctx =>
            ctx.CorrelationId == 778899 &&
            ctx.MessageType == "rabbit.batch.context" &&
            ctx.SentAt == sentAt &&
            ctx.Metadata != null &&
            ctx.Metadata["tenant"] == "acme" &&
            ctx.Metadata["region"] == "apac");
    }

    [SkippableFact]
    public async Task RequestAsync_WithCustomDestination_ShouldRoundTrip()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("request");
        await using var transport = await CreateTransportAsync(options);
        await using var connection = await CreateConnectionAsync(_uri);
        await using var channel = await connection.CreateChannelAsync();
        var requestObserved = new TaskCompletionSource<RabbitMqRemoteCheckStock>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestQueue = $"{options.Prefix}inventory.rpc";

        await channel.QueueDeclareAsync(
            requestQueue,
            durable: options.DurableQueues,
            exclusive: false,
            autoDelete: options.AutoDeleteQueues);
        await channel.QueueBindAsync(requestQueue, options.Exchange, requestQueue);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var request = _serializer.Deserialize<RabbitMqRemoteCheckStock>(ea.Body.ToArray());
            requestObserved.TrySetResult(request);

            var props = new BasicProperties
            {
                CorrelationId = ea.BasicProperties.CorrelationId,
                ContentType = "application/octet-stream"
            };

            await channel.BasicPublishAsync(
                string.Empty,
                ea.BasicProperties.ReplyTo!,
                false,
                props,
                _serializer.Serialize(new RabbitMqRemoteStockResult
                {
                    Qty = 42,
                    Available = true
                }),
                cancellationToken: ea.CancellationToken);
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        };

        await channel.BasicConsumeAsync(requestQueue, autoAck: false, consumer);
        await Task.Delay(300);

        var response = await transport.RequestAsync<RabbitMqRemoteCheckStock, RabbitMqRemoteStockResult>(
            new RabbitMqRemoteCheckStock
            {
                MessageId = MessageExtensions.NewMessageId(),
                Sku = "SKU-RMQ"
            },
            "inventory.rpc",
            TimeSpan.FromSeconds(5));

        response.Should().NotBeNull();
        response!.Available.Should().BeTrue();
        response.Qty.Should().Be(42);

        var observedRequest = await requestObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        observedRequest.Sku.Should().Be("SKU-RMQ");
    }

    [SkippableFact]
    public async Task RequestClient_WithTransportResponder_ShouldRoundTripUsingReplyToMetadata()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("request-responder");
        await using var transport = await CreateTransportAsync(options);
        var requestClientFactory = new RequestClientFactory(transport, TimeSpan.FromSeconds(5));
        var destination = "inventory.reply-route";

        await transport.SubscribeAsync<RabbitMqRemoteCheckStock>(destination, async (request, ctx) =>
        {
            request.Sku.Should().Be("SKU-RMQ-RESPONDER");
            ctx.MessageId.Should().NotBeNull();
            ctx.CorrelationId.Should().NotBeNull();
            ctx.MessageType.Should().Be(typeof(RabbitMqRemoteCheckStock).FullName);
            ctx.Metadata.Should().NotBeNull();
            ctx.Metadata!["reply_to"].Should().NotBeNullOrWhiteSpace();

            await transport.PublishAsync(new RabbitMqRemoteStockResult
            {
                Qty = 33,
                Available = true
            },
            new TransportContext
            {
                Metadata = new Dictionary<string, string> { ["tenant"] = "acme" }
            });
        });

        await Task.Delay(300);

        var client = requestClientFactory.CreateClient<RabbitMqRemoteCheckStock, RabbitMqRemoteStockResult>(destination);
        var result = await client.RequestAsync(new RabbitMqRemoteCheckStock { Sku = "SKU-RMQ-RESPONDER" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Qty.Should().Be(33);
        result.Value.Available.Should().BeTrue();
    }

    [SkippableFact]
    public async Task RabbitMqCompetingConsumer_WhenMaxAttemptsReached_ShouldMoveMessageToDeadLetterQueue()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("competing");
        await using var transport = await CreateTransportAsync(options);
        var queueName = $"{options.Prefix}orders.poison";
        var deadLetterQueue = new RecordingDeadLetterQueue();
        var attempts = 0;

        await using var consumer = new RabbitMqCompetingConsumer<RabbitMqPoisonMessage>(
            _serializer,
            queueName,
            queueName,
            options,
            new CompetingConsumerOptions
            {
                ConsumerName = "rabbit-poison-consumer",
                MaxDeliveryAttempts = 2,
                Concurrency = 1
            },
            deadLetterQueue);

        var consumerTask = Task.Run(() => consumer.StartAsync((_, _) =>
        {
            Interlocked.Increment(ref attempts);
            throw new InvalidOperationException("poison");
        }));

        await Task.Delay(500);

        var poison = new RabbitMqPoisonMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "poison"
        };

        await transport.SendAsync(poison, "orders.poison", new TransportContext { MessageId = poison.MessageId });

        var dlqItem = await deadLetterQueue.WaitForMessageAsync(TimeSpan.FromSeconds(10));
        dlqItem.MessageId.Should().Be(poison.MessageId);
        dlqItem.RetryCount.Should().Be(1);

        await Task.Delay(500);
        Volatile.Read(ref attempts).Should().Be(2);

        await consumer.StopAsync();
        await consumerTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [SkippableFact]
    public async Task Transport_WithCustomPrefix_ShouldWork()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("custom-prefix");
        options.Prefix = $"rmq.custom.{Guid.NewGuid():N}";
        await using var transport = await CreateTransportAsync(options);
        var tcs = new TaskCompletionSource<RabbitMqTestMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<RabbitMqTestMessage>(async (msg, _) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });

        await Task.Delay(300);
        await transport.PublishAsync(new RabbitMqTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "prefix"
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        completed.Should().Be(tcs.Task);
        (await tcs.Task).Data.Should().Be("prefix");
    }

    [SkippableFact]
    public async Task PublishAsync_ShouldEmitDiagnostics()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("diag");
        await using var transport = await CreateTransportAsync(options);
        var activityStarted = false;
        ObservabilityHooks.Enable();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => activityStarted = true
        };
        ActivitySource.AddActivityListener(listener);

        await transport.PublishAsync(new RabbitMqTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "diag"
        });

        activityStarted.Should().BeTrue();
    }

    [SkippableFact]
    public async Task PublishAsync_ShouldPropagateTraceContextToSubscriberActivity()
    {
        Skip.If(_uri is null, "RabbitMQ Docker image is not available locally.");

        var options = CreateOptions("trace");
        await using var transport = await CreateTransportAsync(options);
        string? publishActivityId = null;
        var consumerParentId = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ObservabilityHooks.Enable();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (activity.OperationName == "Messaging.Publish" && activity.Kind == ActivityKind.Producer)
                    publishActivityId ??= activity.Id;
            }
        };
        ActivitySource.AddActivityListener(listener);

        await transport.SubscribeAsync<RabbitMqTestMessage>(async (_, _) =>
        {
            consumerParentId.TrySetResult(Activity.Current?.ParentId);
            await Task.CompletedTask;
        });

        await Task.Delay(300);

        using var root = new Activity("rabbit-root");
        root.Start();

        await transport.PublishAsync(new RabbitMqTestMessage
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "trace"
        });

        var parentId = await consumerParentId.Task.WaitAsync(TimeSpan.FromSeconds(10));
        publishActivityId.Should().NotBeNull();
        parentId.Should().Be(publishActivityId);
    }

    private RabbitMqTransportOptions CreateOptions(string scenario)
        => new()
        {
            Uri = _uri!,
            Exchange = $"catga.test.{scenario}.{Guid.NewGuid():N}",
            Prefix = $"catga.test.{scenario}.{Guid.NewGuid():N}.",
            DurableExchange = false,
            DurableQueues = false,
            AutoDeleteQueues = true,
            PrefetchCount = 1
        };

    private async Task<RabbitMqMessageTransport> CreateTransportAsync(RabbitMqTransportOptions options)
    {
        var transport = new RabbitMqMessageTransport(
            _serializer,
            _provider,
            options,
            NullLogger<RabbitMqMessageTransport>.Instance);
        await transport.InitializeAsync();
        return transport;
    }

    private static async Task EnsureBrokerReadyAsync(string uri)
    {
        await using var connection = await CreateConnectionAsync(uri);
        await connection.DisposeAsync();
    }

    private static async Task<IConnection> CreateConnectionAsync(string uri)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(uri),
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(3)
                };

                return await factory.CreateConnectionAsync();
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(250);
            }
        }

        throw new InvalidOperationException($"Timed out connecting to RabbitMQ at {uri}", lastError);
    }

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
        catch
        {
            return false;
        }
    }

    private static string? ResolveImage(string envVar, string defaultImage)
    {
        var env = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrEmpty(env) ? defaultImage : env;
    }

    private static bool IsImageAvailableLocally(string image)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"image inspect {image}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private sealed class RecordingDeadLetterQueue : IDeadLetterQueue
    {
        private readonly ConcurrentQueue<RecordedDeadLetterMessage> _messages = new();
        private readonly TaskCompletionSource<RecordedDeadLetterMessage> _nextMessage =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendAsync<TMessage>(
            TMessage message,
            Exception exception,
            int retryCount,
            CancellationToken cancellationToken = default)
            where TMessage : IMessage
        {
            var entry = new RecordedDeadLetterMessage
            {
                MessageId = message.MessageId,
                RetryCount = retryCount,
                ExceptionMessage = exception.Message
            };

            _messages.Enqueue(entry);
            _nextMessage.TrySetResult(entry);
            return Task.CompletedTask;
        }

        public Task<List<DeadLetterMessage>> GetFailedMessagesAsync(
            int maxCount = 100,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_messages
                .Take(maxCount)
                .Select(item => new DeadLetterMessage
                {
                    MessageId = item.MessageId,
                    MessageType = typeof(RabbitMqPoisonMessage).FullName ?? nameof(RabbitMqPoisonMessage),
                    Message = [],
                    ExceptionType = nameof(InvalidOperationException),
                    ExceptionMessage = item.ExceptionMessage,
                    StackTrace = string.Empty,
                    RetryCount = item.RetryCount,
                    FailedAt = DateTime.UtcNow
                })
                .ToList());

        public Task<RecordedDeadLetterMessage> WaitForMessageAsync(TimeSpan timeout)
            => _nextMessage.Task.WaitAsync(timeout);
    }

    private sealed class RecordedDeadLetterMessage
    {
        public required long MessageId { get; init; }
        public required int RetryCount { get; init; }
        public required string ExceptionMessage { get; init; }
    }
}

[MemoryPackable]
public partial class RabbitMqTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class RabbitMqRemoteCheckStock : IRequest<RabbitMqRemoteStockResult>
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Sku { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class RabbitMqRemoteStockResult
{
    public int Qty { get; set; }
    public bool Available { get; set; }
}

[MemoryPackable]
public partial class RabbitMqPoisonMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}
