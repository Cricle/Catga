using System.Reflection;
using System.Diagnostics;
using System.Text;
using Catga.Abstractions;
using Catga.Hosting;
using Catga.Observability;
using Catga.Resilience;
using Catga.Transport;
using Catga.Transport.RabbitMQ;
using Catga.Transport.RabbitMQ.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace Catga.Tests.Transport;

public class RabbitMqTransportTests
{
    [Fact]
    public void Transport_ShouldImplementWaitableAndHealthCheckable()
    {
        typeof(RabbitMqMessageTransport).Should().BeAssignableTo<IWaitable>();
        typeof(RabbitMqMessageTransport).Should().BeAssignableTo<IHealthCheckable>();
    }

    [Fact]
    public async Task PublishAsync_ShouldTrackPendingOperationsUntilPublishCompletes()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 1, 2, 3 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => AwaitReleaseAndInvokeAsync(release.Task, callInfo.Arg<Func<CancellationToken, ValueTask>>()));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var transport = CreateReadyTransport(serializer, resilience, channel);

        var publishTask = transport.PublishAsync(new TestMessage());

        await Task.Delay(20);
        transport.PendingOperations.Should().Be(1);

        release.TrySetResult();
        await publishTask;

        transport.PendingOperations.Should().Be(0);
    }

    [Fact]
    public async Task RequestAsync_ShouldTrackPendingOperationsUntilResponseArrives()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var publishChannel = Substitute.For<IChannel>();
        var replyChannel = Substitute.For<IChannel>();
        IAsyncBasicConsumer? replyConsumer = null;
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 10, 11, 12 });
        serializer.Deserialize<TestResponse>(Arg.Any<byte[]>()).Returns(new TestResponse("ok"));
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));

        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(replyChannel));
        replyChannel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new QueueDeclareOk("amq.gen-reply", 0, 0)));
        replyChannel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                replyConsumer = callInfo.ArgAt<IAsyncBasicConsumer>(6);
                return Task.FromResult("consumer-tag");
            });
        publishChannel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var transport = CreateReadyTransport(serializer, resilience, publishChannel, connection);

        var responseTask = transport.RequestAsync<TestMessage, TestResponse>(new TestMessage(), "orders.rpc", TimeSpan.FromSeconds(1));

        await Task.Delay(20);
        transport.PendingOperations.Should().Be(1);

        await replyConsumer!.HandleBasicDeliverAsync(
            "tag",
            1,
            false,
            "catga",
            "catga.orders.rpc",
            new BasicProperties { CorrelationId = GetPendingReplyCorrelationId(transport) },
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            CancellationToken.None);

        var response = await responseTask;
        response.Should().NotBeNull();
        transport.PendingOperations.Should().Be(0);
    }

    [Fact]
    public async Task DisposeAsync_ShouldMarkTransportUnhealthy()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        var connection = Substitute.For<IConnection>();
        var transport = CreateReadyTransport(serializer, resilience, channel, connection);
        SetField(transport, "_isHealthy", true);

        await transport.DisposeAsync();

        transport.IsHealthy.Should().BeFalse();
        transport.HealthStatus.Should().Be("Disconnected");
        transport.IsAcceptingMessages.Should().BeFalse();
        transport.LastHealthCheck.Should().NotBeNull();
    }

    [Fact]
    public async Task AddRabbitMqTransport_ShouldInheritGlobalEndpointNamingConvention()
    {
        var services = new ServiceCollection();
        services.AddCatga(options => options.EndpointNamingConvention = type => $"shop.{type.Name.ToLowerInvariant()}");
        services.AddSingleton(Substitute.For<IMessageSerializer>());
        services.AddSingleton(Substitute.For<IResiliencePipelineProvider>());
        services.AddRabbitMqTransport();

        await using var serviceProvider = services.BuildServiceProvider();
        var transport = serviceProvider.GetRequiredService<IMessageTransport>().Should().BeOfType<RabbitMqMessageTransport>().Subject;

        var optionsField = typeof(RabbitMqMessageTransport).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic);
        optionsField.Should().NotBeNull();
        var options = optionsField!.GetValue(transport).Should().BeOfType<RabbitMqTransportOptions>().Subject;

        options.EndpointNaming.Should().NotBeNull();
        options.EndpointNaming!(typeof(TestMessage)).Should().Be("shop.testmessage");
    }

    [Fact]
    public async Task SendAsync_ShouldPublishToDestinationRoutingKey()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 1, 2, 3 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var transport = CreateReadyTransport(serializer, resilience, channel);

        await transport.SendAsync(new TestMessage(), "orders.direct");

        await channel.Received(1).BasicPublishAsync(
            "catga",
            "catga.orders.direct",
            false,
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WithPrefixMissingTrailingDot_ShouldNormalizeRoutingKey()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 1, 2, 3 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var transport = CreateReadyTransport(
            serializer,
            resilience,
            channel,
            options: new RabbitMqTransportOptions { Prefix = "custom-prefix" });

        await transport.SendAsync(new TestMessage(), "orders.direct");

        await channel.Received(1).BasicPublishAsync(
            "catga",
            "custom-prefix.orders.direct",
            false,
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendBatchAsync_ShouldPublishEachMessageToDestinationRoutingKey()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 4, 5, 6 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var transport = CreateReadyTransport(serializer, resilience, channel);

        await transport.SendBatchAsync([new TestMessage(), new TestMessage()], "billing.jobs");

        await channel.Received(2).BasicPublishAsync(
            "catga",
            "catga.billing.jobs",
            false,
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
        await resilience.Received(2).ExecuteTransportPublishAsync(
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishBatchAsync_WithReplyMetadata_ShouldAlsoPublishEachMessageToReplyQueue()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 2, 4, 6 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var transport = CreateReadyTransport(serializer, resilience, channel);

        await transport.PublishBatchAsync(
            [new TestMessage(), new TestMessage()],
            new TransportContext
            {
                Metadata = new Dictionary<string, string> { ["reply_to"] = "amq.gen-batch" }
            });

        await channel.Received(2).BasicPublishAsync(
            "catga",
            "catga.TestMessage",
            false,
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
        await channel.Received(2).BasicPublishAsync(
            string.Empty,
            "amq.gen-batch",
            false,
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_WithDestination_ShouldBindQueueToDestinationRoutingKey()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        channel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(QueueDeclareOk)!));
        channel.QueueBindAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicQosAsync(Arg.Any<uint>(), Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("consumer-tag"));

        var transport = CreateReadyTransport(serializer, resilience, channel, connection);

        await transport.SubscribeAsync<TestMessage>("orders.queue", (_, _) => Task.CompletedTask);

        await channel.Received(1).QueueDeclareAsync(
            "catga.orders.queue",
            true,
            false,
            false,
            Arg.Any<IDictionary<string, object?>?>(),
            false,
            false,
            Arg.Any<CancellationToken>());
        await channel.Received(1).QueueBindAsync(
            "catga.orders.queue",
            "catga",
            "catga.orders.queue",
            Arg.Any<IDictionary<string, object?>?>(),
            false,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_WithoutDestination_ShouldUseEndpointNamingForQueueAndRoutingKey()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        channel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(QueueDeclareOk)!));
        channel.QueueBindAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicQosAsync(Arg.Any<uint>(), Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("consumer-tag"));

        var transport = CreateReadyTransport(
            serializer,
            resilience,
            channel,
            connection,
            new RabbitMqTransportOptions
            {
                Prefix = "rabbit-prefix",
                EndpointNaming = type => $"shop.{type.Name.ToLowerInvariant()}"
            });

        await transport.SubscribeAsync<TestMessage>((_, _) => Task.CompletedTask);

        await channel.Received(1).QueueDeclareAsync(
            "rabbit-prefix.shop.testmessage",
            true,
            false,
            false,
            Arg.Any<IDictionary<string, object?>?>(),
            false,
            false,
            Arg.Any<CancellationToken>());
        await channel.Received(1).QueueBindAsync(
            "rabbit-prefix.shop.testmessage",
            "catga",
            "rabbit-prefix.shop.testmessage",
            Arg.Any<IDictionary<string, object?>?>(),
            false,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_ShouldPopulateTransportMetadata()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        BasicProperties? capturedProperties = null;
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 7, 8, 9 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(callInfo => capturedProperties = callInfo.ArgAt<BasicProperties>(3));

        var transport = CreateReadyTransport(serializer, resilience, channel);

        await transport.PublishAsync(new TestMessage(),
            new TransportContext
            {
                MessageId = 42,
                CorrelationId = 99,
                MessageType = "tests.message"
            });

        capturedProperties.Should().NotBeNull();
        capturedProperties!.MessageId.Should().Be("42");
        capturedProperties.CorrelationId.Should().Be("99");
        capturedProperties.Type.Should().Be("tests.message");
    }

    [Fact]
    public async Task PublishAsync_ShouldPopulateCustomHeadersAndTraceContext()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        BasicProperties? capturedProperties = null;
        var sentAt = new DateTime(2024, 01, 02, 03, 04, 05, DateTimeKind.Utc);
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 3, 2, 1 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(callInfo => capturedProperties = callInfo.ArgAt<BasicProperties>(3));

        var transport = CreateReadyTransport(serializer, resilience, channel);

        using var activity = new Activity("rabbit-test-parent");
        activity.Start();

        await transport.PublishAsync(new TestMessage(),
            new TransportContext
            {
                MessageId = 88,
                CorrelationId = 77,
                MessageType = "tests.header.message",
                SentAt = sentAt,
                Metadata = new Dictionary<string, string> { ["tenant"] = "acme" }
            });

        capturedProperties.Should().NotBeNull();
        capturedProperties!.Headers.Should().NotBeNull();
        capturedProperties.Headers!["meta.tenant"].Should().Be("acme");
        capturedProperties.Headers.Should().ContainKey("sent_at");
        capturedProperties.Timestamp.UnixTime.Should().Be(new DateTimeOffset(sentAt).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task PublishAsync_WithPriorityMetadata_ShouldPopulateRabbitPriorityAndRespectConfiguredMax()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        BasicProperties? capturedProperties = null;
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 8, 8, 8 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(callInfo => capturedProperties = callInfo.ArgAt<BasicProperties>(3));

        var transport = CreateReadyTransport(
            serializer,
            resilience,
            channel,
            options: new RabbitMqTransportOptions { MaxPriority = 2 });

        await transport.PublishAsync(
            new TestMessage(),
            new TransportContext
            {
                Metadata = new Dictionary<string, string> { ["x-priority"] = "3" }
            });

        capturedProperties.Should().NotBeNull();
        capturedProperties!.Priority.Should().Be(2);
    }

    [Fact]
    public async Task PublishAsync_WithDelayedMessage_ShouldPopulateXDelayHeader()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        BasicProperties? capturedProperties = null;
        serializer.Serialize(Arg.Any<TestDelayedMessage>()).Returns(new byte[] { 7, 7, 7 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(callInfo => capturedProperties = callInfo.ArgAt<BasicProperties>(3));

        var transport = CreateReadyTransport(serializer, resilience, channel);

        await transport.PublishAsync(
            new TestDelayedMessage
            {
                MessageId = 77,
                ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(5)
            });

        capturedProperties.Should().NotBeNull();
        capturedProperties!.Headers.Should().NotBeNull();
        capturedProperties.Headers!.Should().ContainKey("x-delay");
        capturedProperties.Headers["x-delay"].Should().BeOfType<int>();
        ((int)capturedProperties.Headers["x-delay"]!).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PublishAsync_WithPrioritizedMessage_ShouldPopulateRabbitPriorityWithoutExplicitMetadata()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        BasicProperties? capturedProperties = null;
        serializer.Serialize(Arg.Any<TestPrioritizedRabbitMessage>()).Returns(new byte[] { 4, 4, 4 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(callInfo => capturedProperties = callInfo.ArgAt<BasicProperties>(3));

        var transport = CreateReadyTransport(
            serializer,
            resilience,
            channel,
            options: new RabbitMqTransportOptions { MaxPriority = 3 });

        await transport.PublishAsync(new TestPrioritizedRabbitMessage { Priority = MessagePriority.High });

        capturedProperties.Should().NotBeNull();
        capturedProperties!.Priority.Should().Be(2);
    }

    [Fact]
    public void DelayedExchangeHelper_ShouldUseDelayedExchangeTypeAndUnderlyingTypeArgument()
    {
        var helperType = Type.GetType("Catga.Transport.RabbitMQ.RabbitMqTransportDelay, Catga.Transport.RabbitMQ");
        helperType.Should().NotBeNull();

        var resolveExchangeType = helperType!.GetMethod("ResolveExchangeType", BindingFlags.Public | BindingFlags.Static);
        var buildExchangeArguments = helperType.GetMethod("BuildExchangeArguments", BindingFlags.Public | BindingFlags.Static);
        resolveExchangeType.Should().NotBeNull();
        buildExchangeArguments.Should().NotBeNull();

        var options = new RabbitMqTransportOptions
        {
            ExchangeType = "topic",
            UseDelayedExchange = true
        };

        var declaredType = resolveExchangeType!.Invoke(null, [options]);
        var arguments = buildExchangeArguments!.Invoke(null, [options]) as IDictionary<string, object?>;

        declaredType.Should().Be("x-delayed-message");
        arguments.Should().NotBeNull();
        arguments!["x-delayed-type"].Should().Be("topic");
    }

    [Fact]
    public async Task PublishAsync_WithObservabilityEnabled_ShouldPopulateTraceparentWithoutAmbientActivity()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        BasicProperties? capturedProperties = null;
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 5, 5, 5 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(callInfo => capturedProperties = callInfo.ArgAt<BasicProperties>(3));

        var transport = CreateReadyTransport(serializer, resilience, channel);
        ObservabilityHooks.Enable();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        await transport.PublishAsync(new TestMessage(), new TransportContext { MessageId = 501 });

        capturedProperties.Should().NotBeNull();
        capturedProperties!.Headers.Should().NotBeNull();
        capturedProperties.Headers.Should().ContainKey("traceparent");
        capturedProperties.Headers["traceparent"].Should().NotBeNull();
    }

    [Fact]
    public async Task PublishAsync_ShouldUsePublishActivityForTraceparentInsteadOfAmbientActivity()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        BasicProperties? capturedProperties = null;
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 6, 6, 6 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(callInfo => capturedProperties = callInfo.ArgAt<BasicProperties>(3));

        var transport = CreateReadyTransport(serializer, resilience, channel);
        ObservabilityHooks.Enable();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var ambient = new Activity("rabbit-ambient-root");
        ambient.Start();

        await transport.PublishAsync(new TestMessage(), new TransportContext { MessageId = 601 });

        capturedProperties.Should().NotBeNull();
        capturedProperties!.Headers.Should().NotBeNull();
        capturedProperties.Headers.Should().ContainKey("traceparent");
        capturedProperties.Headers["traceparent"]!.ToString().Should().NotBe(ambient.Id);
    }

    [Fact]
    public async Task SubscribeAsync_ShouldRestoreMetadataFromRabbitMqHeaders()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        serializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(new TestMessage());
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        channel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(QueueDeclareOk)!));
        channel.QueueBindAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicQosAsync(Arg.Any<uint>(), Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IAsyncBasicConsumer? consumer = null;
        channel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                consumer = callInfo.ArgAt<IAsyncBasicConsumer>(6);
                return Task.FromResult("consumer-tag");
            });

        var transport = CreateReadyTransport(serializer, resilience, channel, connection);
        TransportContext received = default;

        await transport.SubscribeAsync<TestMessage>("orders.queue", (_, ctx) =>
        {
            received = ctx;
            return Task.CompletedTask;
        });

        var props = new BasicProperties
        {
            MessageId = "101",
            CorrelationId = "202",
            Type = "tests.restore.message",
            Headers = new Dictionary<string, object?>
            {
                ["sent_at"] = DateTime.UtcNow.ToString("O"),
                ["meta.tenant"] = Encoding.UTF8.GetBytes("acme"),
                ["meta.region"] = "apac"
            }
        };

        await consumer!.HandleBasicDeliverAsync(
            "tag",
            7,
            false,
            "catga",
            "catga.orders.queue",
            props,
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            CancellationToken.None);

        received.MessageId.Should().Be(101);
        received.CorrelationId.Should().Be(202);
        received.MessageType.Should().Be("tests.restore.message");
        received.Metadata.Should().NotBeNull();
        received.Metadata!["tenant"].Should().Be("acme");
        received.Metadata["region"].Should().Be("apac");
    }

    [Fact]
    public async Task SubscribeAsync_WithoutSentAtHeader_ShouldRestoreSentAtFromRabbitTimestamp()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        serializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(new TestMessage());
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        channel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(QueueDeclareOk)!));
        channel.QueueBindAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicQosAsync(Arg.Any<uint>(), Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IAsyncBasicConsumer? consumer = null;
        channel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                consumer = callInfo.ArgAt<IAsyncBasicConsumer>(6);
                return Task.FromResult("consumer-tag");
            });

        var transport = CreateReadyTransport(serializer, resilience, channel, connection);
        TransportContext received = default;

        await transport.SubscribeAsync<TestMessage>("orders.queue", (_, ctx) =>
        {
            received = ctx;
            return Task.CompletedTask;
        });

        var sentAt = new DateTime(2024, 08, 09, 10, 11, 12, DateTimeKind.Utc);
        var props = new BasicProperties
        {
            Timestamp = new AmqpTimestamp(new DateTimeOffset(sentAt).ToUnixTimeSeconds())
        };

        await consumer!.HandleBasicDeliverAsync(
            "tag",
            7,
            false,
            "catga",
            "catga.orders.queue",
            props,
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            CancellationToken.None);

        received.SentAt.Should().Be(sentAt);
    }

    [Fact]
    public async Task SubscribeAsync_WithCaseInsensitiveHeaders_ShouldRestoreTransportMetadata()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        serializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(new TestMessage());
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        channel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(QueueDeclareOk)!));
        channel.QueueBindAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicQosAsync(Arg.Any<uint>(), Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IAsyncBasicConsumer? consumer = null;
        channel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                consumer = callInfo.ArgAt<IAsyncBasicConsumer>(6);
                return Task.FromResult("consumer-tag");
            });

        var transport = CreateReadyTransport(serializer, resilience, channel, connection);
        TransportContext received = default;

        await transport.SubscribeAsync<TestMessage>("orders.queue", (_, ctx) =>
        {
            received = ctx;
            return Task.CompletedTask;
        });

        var props = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                ["MessageId"] = "9004",
                ["CorrelationId"] = "12345",
                ["MessageType"] = "external.mixed.case",
                ["SentAt"] = "2024-04-05T06:07:08.0000000Z",
                ["Meta-Tenant"] = Encoding.UTF8.GetBytes("acme"),
                ["Meta-Reply-To"] = "amq.gen-hyphen",
                ["X-Delay"] = 1500,
                ["TraceParent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
            }
        };

        await consumer!.HandleBasicDeliverAsync(
            "tag",
            7,
            false,
            "catga",
            "catga.orders.queue",
            props,
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            CancellationToken.None);

        received.MessageId.Should().Be(9004);
        received.CorrelationId.Should().Be(12345);
        received.MessageType.Should().Be("external.mixed.case");
        received.SentAt.Should().Be(new DateTime(2024, 04, 05, 06, 07, 08, DateTimeKind.Utc));
        received.Metadata.Should().NotBeNull();
        received.Metadata!["Tenant"].Should().Be("acme");
        received.Metadata["reply_to"].Should().Be("amq.gen-hyphen");
        received.Metadata["reply_subject"].Should().Be("amq.gen-hyphen");
        received.Metadata["x-delay"].Should().Be("1500");
    }

    [Fact]
    public async Task SubscribeAsync_WithHeaderOnlyMessageIdentity_ShouldPopulateReceiveActivityTags()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        serializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(new TestMessage());
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        channel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(QueueDeclareOk)!));
        channel.QueueBindAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicQosAsync(Arg.Any<uint>(), Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IAsyncBasicConsumer? consumer = null;
        channel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                consumer = callInfo.ArgAt<IAsyncBasicConsumer>(6);
                return Task.FromResult("consumer-tag");
            });

        var transport = CreateReadyTransport(serializer, resilience, channel, connection);
        string? activityMessageType = null;
        string? activityMessageId = null;
        ObservabilityHooks.Enable();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CatgaActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        await transport.SubscribeAsync<TestMessage>("orders.queue", (_, _) =>
        {
            activityMessageType = Activity.Current?.GetTagItem(CatgaActivitySource.Tags.MessageType)?.ToString();
            activityMessageId = Activity.Current?.GetTagItem(CatgaActivitySource.Tags.MessageId)?.ToString();
            return Task.CompletedTask;
        });

        var props = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                ["MessageId"] = "9005",
                ["MessageType"] = "external.activity.type"
            }
        };

        await consumer!.HandleBasicDeliverAsync(
            "tag",
            7,
            false,
            "catga",
            "catga.orders.queue",
            props,
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            CancellationToken.None);

        activityMessageType.Should().Be("external.activity.type");
        activityMessageId.Should().Be("9005");
    }

    [Fact]
    public async Task SubscribeAsync_ShouldBackfillPriorityAndDelayMetadataFromNativeRabbitProperties()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        serializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(new TestMessage());
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        channel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(QueueDeclareOk)!));
        channel.QueueBindAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicQosAsync(Arg.Any<uint>(), Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IAsyncBasicConsumer? consumer = null;
        channel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                consumer = callInfo.ArgAt<IAsyncBasicConsumer>(6);
                return Task.FromResult("consumer-tag");
            });

        var transport = CreateReadyTransport(serializer, resilience, channel, connection);
        TransportContext received = default;

        await transport.SubscribeAsync<TestMessage>("orders.queue", (_, ctx) =>
        {
            received = ctx;
            return Task.CompletedTask;
        });

        var props = new BasicProperties
        {
            Priority = 3,
            Headers = new Dictionary<string, object?>
            {
                ["x-delay"] = 1500
            }
        };

        await consumer!.HandleBasicDeliverAsync(
            "tag",
            7,
            false,
            "catga",
            "catga.orders.queue",
            props,
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            CancellationToken.None);

        received.Metadata.Should().NotBeNull();
        received.Metadata!["x-priority"].Should().Be("3");
        received.Metadata["x-delay"].Should().Be("1500");
    }

    [Fact]
    public async Task SubscribeAsync_WithMaxPriorityConfigured_ShouldDeclareQueueWithPriorityArgument()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        IDictionary<string, object?>? declaredArguments = null;
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        channel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(QueueDeclareOk)!))
            .AndDoes(callInfo => declaredArguments = callInfo.ArgAt<IDictionary<string, object?>?>(4));
        channel.QueueBindAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicQosAsync(Arg.Any<uint>(), Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("consumer-tag"));

        var transport = CreateReadyTransport(
            serializer,
            resilience,
            channel,
            connection,
            new RabbitMqTransportOptions { MaxPriority = 4 });

        await transport.SubscribeAsync<TestMessage>("orders.queue", (_, _) => Task.CompletedTask);

        declaredArguments.Should().NotBeNull();
        declaredArguments!["x-max-priority"].Should().Be(4);
    }

    [Fact]
    public async Task SubscribeAsync_ShouldExposeReplyToInTransportContextMetadata()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();
        serializer.Deserialize<TestMessage>(Arg.Any<byte[]>()).Returns(new TestMessage());
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(channel));
        channel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(default(QueueDeclareOk)!));
        channel.QueueBindAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        channel.BasicQosAsync(Arg.Any<uint>(), Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        IAsyncBasicConsumer? consumer = null;
        channel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                consumer = callInfo.ArgAt<IAsyncBasicConsumer>(6);
                return Task.FromResult("consumer-tag");
            });

        var transport = CreateReadyTransport(serializer, resilience, channel, connection);
        TransportContext received = default;

        await transport.SubscribeAsync<TestMessage>("orders.queue", (_, ctx) =>
        {
            received = ctx;
            return Task.CompletedTask;
        });

        var props = new BasicProperties
        {
            ReplyTo = "amq.gen-request"
        };

        await consumer!.HandleBasicDeliverAsync(
            "tag",
            8,
            false,
            "catga",
            "catga.orders.queue",
            props,
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            CancellationToken.None);

        received.Metadata.Should().NotBeNull();
        received.Metadata!["reply_to"].Should().Be("amq.gen-request");
    }

    [Fact]
    public async Task PublishAsync_WithReplyMetadata_ShouldAlsoPublishToReplyQueue()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var channel = Substitute.For<IChannel>();
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 9, 9, 9 });
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));
        channel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var transport = CreateReadyTransport(serializer, resilience, channel);

        await transport.PublishAsync(
            new TestMessage(),
            new TransportContext
            {
                CorrelationId = 333,
                Metadata = new Dictionary<string, string> { ["reply_to"] = "amq.gen-rpc" }
            });

        await channel.Received(1).BasicPublishAsync(
            "catga",
            "catga.TestMessage",
            false,
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
        await channel.Received(1).BasicPublishAsync(
            string.Empty,
            "amq.gen-rpc",
            false,
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestAsync_ShouldUseResolvedDestinationRoutingKey()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var publishChannel = Substitute.For<IChannel>();
        var replyChannel = Substitute.For<IChannel>();
        IAsyncBasicConsumer? replyConsumer = null;
        serializer.Serialize(Arg.Any<TestMessage>()).Returns(new byte[] { 10, 11, 12 });
        serializer.Deserialize<TestResponse>(Arg.Any<byte[]>()).Returns(new TestResponse("ok"));
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));

        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(replyChannel));
        replyChannel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new QueueDeclareOk("amq.gen-reply", 0, 0)));
        replyChannel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                replyConsumer = callInfo.ArgAt<IAsyncBasicConsumer>(6);
                return Task.FromResult("consumer-tag");
            });

        string? publishedRoutingKey = null;
        string? publishedCorrelationId = null;
        publishChannel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(callInfo =>
            {
                publishedRoutingKey = callInfo.ArgAt<string>(1);
                publishedCorrelationId = callInfo.ArgAt<BasicProperties>(3).CorrelationId;
            });

        var transport = CreateReadyTransport(serializer, resilience, publishChannel, connection);

        var responseTask = transport.RequestAsync<TestMessage, TestResponse>(new TestMessage(), "orders.rpc", TimeSpan.FromSeconds(1));

        await Task.Delay(20);

        publishedRoutingKey.Should().Be("catga.orders.rpc");
        publishedCorrelationId.Should().NotBeNullOrWhiteSpace();

        replyConsumer.Should().NotBeNull();
        var responseProperties = new BasicProperties { CorrelationId = publishedCorrelationId };
        await replyConsumer!.HandleBasicDeliverAsync(
            "tag",
            1,
            false,
            "catga",
            "catga.orders.rpc",
            responseProperties,
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            CancellationToken.None);

        var response = await responseTask;
        response.Should().NotBeNull();
        response!.Status.Should().Be("ok");
    }

    [Fact]
    public async Task RequestAsync_ShouldAssignMessageMetadataAndReplyTo()
    {
        var serializer = Substitute.For<IMessageSerializer>();
        var resilience = Substitute.For<IResiliencePipelineProvider>();
        var connection = Substitute.For<IConnection>();
        var publishChannel = Substitute.For<IChannel>();
        var replyChannel = Substitute.For<IChannel>();
        IAsyncBasicConsumer? replyConsumer = null;
        TestMessage? serializedMessage = null;
        BasicProperties? publishedProperties = null;

        serializer.Serialize(Arg.Do<TestMessage>(m => serializedMessage = m)).Returns(new byte[] { 5, 6, 7 });
        serializer.Deserialize<TestResponse>(Arg.Any<byte[]>()).Returns(new TestResponse("ok"));
        resilience.ExecuteTransportPublishAsync(Arg.Any<Func<CancellationToken, ValueTask>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<CancellationToken, ValueTask>>().Invoke(CancellationToken.None));

        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(replyChannel));
        replyChannel.QueueDeclareAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new QueueDeclareOk("amq.gen-reply", 0, 0)));
        replyChannel.BasicConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IDictionary<string, object?>?>(),
                Arg.Any<IAsyncBasicConsumer>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                replyConsumer = callInfo.ArgAt<IAsyncBasicConsumer>(6);
                return Task.FromResult("consumer-tag");
            });
        publishChannel.BasicPublishAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<BasicProperties>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask)
            .AndDoes(callInfo => publishedProperties = callInfo.ArgAt<BasicProperties>(3));

        var transport = CreateReadyTransport(serializer, resilience, publishChannel, connection);

        var responseTask = transport.RequestAsync<TestMessage, TestResponse>(new TestMessage(), "orders.rpc", TimeSpan.FromSeconds(1));

        await Task.Delay(20);

        serializedMessage.Should().NotBeNull();
        serializedMessage!.MessageId.Should().NotBe(0);
        serializedMessage.CorrelationId.Should().NotBeNull();
        publishedProperties.Should().NotBeNull();
        publishedProperties!.ReplyTo.Should().Be("amq.gen-reply");
        publishedProperties.MessageId.Should().Be(serializedMessage.MessageId.ToString());
        publishedProperties.CorrelationId.Should().Be(serializedMessage.CorrelationId!.Value.ToString());

        var responseProperties = new BasicProperties { CorrelationId = publishedProperties.CorrelationId };
        await replyConsumer!.HandleBasicDeliverAsync(
            "tag",
            1,
            false,
            "catga",
            "catga.orders.rpc",
            responseProperties,
            new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
            CancellationToken.None);

        var response = await responseTask;
        response.Should().NotBeNull();
        response!.Status.Should().Be("ok");
    }

    private static RabbitMqMessageTransport CreateReadyTransport(
        IMessageSerializer serializer,
        IResiliencePipelineProvider resilience,
        IChannel channel,
        IConnection? connection = null,
        RabbitMqTransportOptions? options = null)
    {
        var transport = new RabbitMqMessageTransport(serializer, resilience, options);
        SetField(transport, "_publishChannel", channel);
        if (connection != null)
            SetField(transport, "_connection", connection);
        return transport;
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field {name} should exist");
        field!.SetValue(target, value);
    }

    private static string GetPendingReplyCorrelationId(RabbitMqMessageTransport transport)
    {
        var field = transport.GetType().GetField("_pendingReplies", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var pendingReplies = field!.GetValue(transport) as System.Collections.IDictionary;
        pendingReplies.Should().NotBeNull();
        pendingReplies!.Count.Should().Be(1);
        return pendingReplies.Keys.Cast<string>().Single();
    }

    private static async ValueTask AwaitReleaseAndInvokeAsync(Task releaseTask, Func<CancellationToken, ValueTask> action)
    {
        await releaseTask;
        await action(CancellationToken.None);
    }

    public sealed class TestMessage : IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
    }

    public sealed class TestDelayedMessage : IDelayedMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public DateTimeOffset? ScheduledAt { get; init; }
        public TimeSpan? Delay { get; init; }
    }

    public sealed class TestPrioritizedRabbitMessage : IPrioritizedMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public MessagePriority Priority { get; init; } = MessagePriority.Normal;
    }

    public sealed record TestResponse(string Status);
}
